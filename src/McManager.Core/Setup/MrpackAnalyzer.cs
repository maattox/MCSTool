using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Parses a user-supplied Modrinth <c>.mrpack</c> locally (blueprint §22 / §2.4).
/// No catalog/search HTTP. Download URLs inside the index are not fetched.
/// Applies itzg/product exclude lists after <c>env.server</c> (robustness R2),
/// then leftover in-jar client signals for embedded jars (Step 8.7 P3).
/// </summary>
public static class MrpackAnalyzer
{
    public const string IndexEntryName = "modrinth.index.json";
    public const int SupportedFormatVersion = 1;
    public const string ExpectedGame = "minecraft";

    public const string LoaderFabric = "fabric";
    public const string LoaderQuilt = "quilt";
    public const string LoaderForge = "forge";
    public const string LoaderNeoForge = "neoforge";

    public const string EnvRequired = "required";
    public const string EnvOptional = "optional";
    public const string EnvUnsupported = "unsupported";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly (string DependencyKey, string LoaderId)[] LoaderKeys =
    [
        ("fabric-loader", LoaderFabric),
        ("quilt-loader", LoaderQuilt),
        ("neoforge", LoaderNeoForge),
        ("forge", LoaderForge),
    ];

    private static readonly Lazy<ExcludeIncludeMatcher> DefaultMatcher =
        new(() => ExcludeIncludeMatcher.ForModrinth());

    public static ServiceResult<MrpackAnalysis> AnalyzeFile(
        string path,
        ExcludeIncludeMatcher? matcher = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ServiceResult<MrpackAnalysis>.Fail("No .mrpack path was provided.");
        if (!File.Exists(path))
            return ServiceResult<MrpackAnalysis>.Fail($"File not found: {path}");

        try
        {
            using var stream = File.OpenRead(path);
            return AnalyzeZip(stream, Path.GetFileName(path), matcher);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ServiceResult<MrpackAnalysis>.Fail($"Cannot read .mrpack: {ex.Message}");
        }
        catch (IOException ex)
        {
            return ServiceResult<MrpackAnalysis>.Fail($"Cannot read .mrpack: {ex.Message}");
        }
    }

    public static ServiceResult<MrpackAnalysis> AnalyzeZip(
        Stream zipStream,
        string? sourceName = null,
        ExcludeIncludeMatcher? matcher = null)
    {
        ArgumentNullException.ThrowIfNull(zipStream);

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            var label = string.IsNullOrWhiteSpace(sourceName) ? "file" : sourceName;
            return ServiceResult<MrpackAnalysis>.Fail(
                $"{label} is not a valid ZIP/.mrpack archive.");
        }
        catch (NotSupportedException)
        {
            return ServiceResult<MrpackAnalysis>.Fail("This archive uses an unsupported ZIP feature.");
        }

        using (zip)
        {
            var entryNames = zip.Entries
                .Select(e => NormalizeZipPath(e.FullName))
                .Where(n => n.Length > 0)
                .ToList();

            var indexEntry = zip.Entries.FirstOrDefault(e =>
                string.Equals(NormalizeZipPath(e.FullName), IndexEntryName, StringComparison.OrdinalIgnoreCase));
            if (indexEntry is null)
            {
                var nested = zip.Entries.FirstOrDefault(e =>
                    NormalizeZipPath(e.FullName)
                        .EndsWith("/" + IndexEntryName, StringComparison.OrdinalIgnoreCase));
                if (nested is not null)
                {
                    return ServiceResult<MrpackAnalysis>.Fail(
                        $"{IndexEntryName} must be at the archive root (found '{NormalizeZipPath(nested.FullName)}').");
                }

                return ServiceResult<MrpackAnalysis>.Fail(
                    $"Not a Modrinth pack: missing {IndexEntryName} at the archive root.");
            }

            string json;
            using (var reader = new StreamReader(indexEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                json = reader.ReadToEnd();

            return AnalyzeIndexJson(json, entryNames, matcher, sourceName, zip);
        }
    }

    public static ServiceResult<MrpackAnalysis> AnalyzeIndexJson(
        string json,
        IReadOnlyList<string>? zipEntryNames = null,
        ExcludeIncludeMatcher? matcher = null,
        string? sourceName = null,
        ZipArchive? zip = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ServiceResult<MrpackAnalysis>.Fail($"{IndexEntryName} is empty.");

        MrpackIndexDocument? index;
        try
        {
            index = JsonSerializer.Deserialize<MrpackIndexDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ServiceResult<MrpackAnalysis>.Fail($"Invalid {IndexEntryName}: {ex.Message}");
        }

        if (index is null)
            return ServiceResult<MrpackAnalysis>.Fail($"{IndexEntryName} deserialized to null.");

        return AnalyzeIndex(index, zipEntryNames, matcher, sourceName, zip);
    }

    internal static ServiceResult<MrpackAnalysis> AnalyzeIndex(
        MrpackIndexDocument index,
        IReadOnlyList<string>? zipEntryNames,
        ExcludeIncludeMatcher? matcher = null,
        string? sourceName = null,
        ZipArchive? zip = null)
    {
        if (index.FormatVersion != SupportedFormatVersion)
        {
            return ServiceResult<MrpackAnalysis>.Fail(
                $"{IndexEntryName} formatVersion must be {SupportedFormatVersion} (got {index.FormatVersion}).");
        }

        if (!string.Equals((index.Game ?? "").Trim(), ExpectedGame, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<MrpackAnalysis>.Fail(
                $"Pack game must be '{ExpectedGame}' (got '{index.Game ?? "(missing)"}').");
        }

        var deps = index.Dependencies ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetDependency(deps, "minecraft", out var minecraftVersion))
        {
            return ServiceResult<MrpackAnalysis>.Fail(
                "Pack dependencies must include minecraft (the Minecraft version).");
        }

        if (!TrySelectLoader(deps, out var loader, out var loaderVersion, out var loaderError))
            return ServiceResult<MrpackAnalysis>.Fail(loaderError!);

        var warnings = new List<string>();
        int? javaMajor = null;
        if (MinecraftJavaFloor.TryGet(minecraftVersion, out var mappedJava))
            javaMajor = mappedJava;
        else
            warnings.Add($"Could not map Minecraft {minecraftVersion} to a Java major (blueprint §9.1).");

        var names = zipEntryNames ?? [];
        var lists = matcher ?? DefaultMatcher.Value;
        var packName = string.IsNullOrWhiteSpace(index.Name) ? "(unnamed pack)" : index.Name.Trim();
        if (string.IsNullOrWhiteSpace(index.Name))
            warnings.Add("Pack name is missing in modrinth.index.json.");

        var versionId = string.IsNullOrWhiteSpace(index.VersionId) ? null : index.VersionId.Trim();
        var summary = string.IsNullOrWhiteSpace(index.Summary) ? null : index.Summary.Trim();
        var packSlug = MrpackFileFilter.ResolvePackSlug(lists, packName, versionId, sourceName);

        var files = index.Files ?? [];
        var serverRequired = new List<string>();
        var serverOptional = new List<string>();
        var packDeclared = new List<string>();
        var overrideList = new List<string>();
        var inJarSkip = new List<string>();
        var forceIncluded = new List<string>();
        var unclear = new List<string>();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var path = string.IsNullOrWhiteSpace(file.Path)
                ? $"(unnamed file #{i + 1})"
                : file.Path.Trim().Replace('\\', '/');
            var serverEnv = (file.Env?.Server ?? "").Trim();
            var match = lists.Match(packSlug, path);
            var inJar = PeekEmbeddedJar(zip, path);
            var action = MrpackFileFilter.Decide(serverEnv, match, inJar.Environment);

            if (action == MrpackFileFilter.Action.Install && !HasDownloadUrl(file) && !ZipHasIndexedFile(names, path))
            {
                warnings.Add(
                    $"File '{path}' has no download URL in the index (install will copy from the zip if present).");
            }

            switch (action)
            {
                case MrpackFileFilter.Action.SkipPackDeclared:
                    packDeclared.Add(path);
                    continue;
                case MrpackFileFilter.Action.SkipOverrideList:
                    overrideList.Add(path);
                    continue;
                case MrpackFileFilter.Action.SkipInJarMetadata:
                    inJarSkip.Add(path);
                    continue;
                case MrpackFileFilter.Action.Unclear:
                    unclear.Add(path);
                    if (serverEnv.Length > 0)
                    {
                        warnings.Add(
                            $"File '{path}' has unknown env.server '{file.Env!.Server}' (expected required, optional, or unsupported).");
                    }

                    continue;
                default:
                    if (match.Keep && serverEnv.Equals(EnvUnsupported, StringComparison.OrdinalIgnoreCase))
                        forceIncluded.Add(path);
                    if (serverEnv.Equals(EnvOptional, StringComparison.OrdinalIgnoreCase))
                        serverOptional.Add(path);
                    else
                        serverRequired.Add(path);
                    continue;
            }
        }

        if (inJarSkip.Count > 0)
        {
            warnings.Add(
                $"{inJarSkip.Count} file(s) detected as client-only from in-jar metadata will not be installed.");
        }

        if (unclear.Count > 0)
        {
            warnings.Add(
                $"{unclear.Count} file(s) have unclear server/client side. Do not install those until reviewed (fail/warn in the install step).");
        }

        var hasOverrides = names.Any(n => n.Equals("overrides", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase));
        var hasServerOverrides = names.Any(n => n.Equals("server-overrides", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("server-overrides/", StringComparison.OrdinalIgnoreCase));
        var hasClientOverrides = names.Any(n => n.Equals("client-overrides", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase));

        var clientOnly = packDeclared.Concat(overrideList).Concat(inJarSkip).ToList();
        var serverSide = serverRequired.Concat(serverOptional).ToList();
        var confirmable = BuildConfirmableSummary(
            packName,
            versionId,
            summary,
            minecraftVersion,
            loader,
            loaderVersion,
            javaMajor,
            files.Count,
            serverRequired.Count,
            serverOptional.Count,
            clientOnly.Count,
            unclear.Count,
            packDeclared,
            overrideList,
            inJarSkip,
            unclear,
            hasOverrides,
            hasServerOverrides,
            hasClientOverrides,
            warnings);

        return ServiceResult<MrpackAnalysis>.Ok(new MrpackAnalysis(
            packName,
            versionId,
            summary,
            minecraftVersion,
            loader,
            loaderVersion,
            javaMajor,
            files.Count,
            serverRequired.Count,
            serverOptional.Count,
            clientOnly.Count,
            unclear.Count,
            serverSide,
            clientOnly,
            unclear,
            hasOverrides,
            hasServerOverrides,
            hasClientOverrides,
            warnings,
            confirmable,
            packDeclared.Count,
            overrideList.Count,
            packDeclared,
            overrideList,
            forceIncluded,
            inJarSkip.Count,
            inJarSkip));
    }

    public static bool TrySelectLoader(
        IReadOnlyDictionary<string, string> dependencies,
        out string loader,
        out string loaderVersion,
        out string? error)
    {
        loader = "";
        loaderVersion = "";
        error = null;

        var matched = new List<(string LoaderId, string Version, string Key)>();
        foreach (var (key, loaderId) in LoaderKeys)
        {
            if (TryGetDependency(dependencies, key, out var version))
                matched.Add((loaderId, version, key));
        }

        if (matched.Count == 0)
        {
            error =
                "Pack dependencies have no recognized loader (expected fabric-loader, quilt-loader, forge, or neoforge).";
            return false;
        }

        if (matched.Count > 1)
        {
            error = "Pack dependencies list more than one loader: "
                + string.Join(", ", matched.Select(m => $"{m.Key}={m.Version}"))
                + ".";
            return false;
        }

        loader = matched[0].LoaderId;
        loaderVersion = matched[0].Version;
        return true;
    }

    public static string NormalizeZipPath(string fullName)
    {
        var n = (fullName ?? "").Replace('\\', '/').Trim();
        while (n.StartsWith("./", StringComparison.Ordinal))
            n = n[2..];
        return n.TrimStart('/');
    }

    internal static bool HasDownloadUrl(MrpackIndexFile file) =>
        file.Downloads is not null && file.Downloads.Any(u => !string.IsNullOrWhiteSpace(u));

    internal static bool ZipHasIndexedFile(IReadOnlyList<string> zipEntryNames, string relativePath)
    {
        var n = relativePath.Replace('\\', '/').Trim();
        if (n.Length == 0 || zipEntryNames.Count == 0)
            return false;
        foreach (var entry in zipEntryNames)
        {
            if (entry.Equals(n, StringComparison.OrdinalIgnoreCase)
                || entry.Equals(MrpackInstaller.OverridesPrefix + n, StringComparison.OrdinalIgnoreCase)
                || entry.Equals(MrpackInstaller.ServerOverridesPrefix + n, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static InJarSideDetector.PeekResult PeekEmbeddedJar(ZipArchive? zip, string relativePath)
    {
        if (zip is null || !MrpackFileFilter.IsJarPath(relativePath))
            return InJarSideDetector.PeekResult.None;
        var entry = FindEmbeddedEntry(zip, relativePath);
        if (entry is null)
            return InJarSideDetector.PeekResult.None;
        return ManualServerPackAnalyzer.PeekJarEnvironment(entry);
    }

    internal static ZipArchiveEntry? FindEmbeddedEntry(ZipArchive zip, string relativePath)
    {
        var n = relativePath.Replace('\\', '/').Trim();
        if (n.Length == 0)
            return null;
        string[] candidates =
        [
            n,
            MrpackInstaller.OverridesPrefix + n,
            MrpackInstaller.ServerOverridesPrefix + n,
        ];
        foreach (var candidate in candidates)
        {
            var found = zip.Entries.FirstOrDefault(e =>
                string.Equals(
                    NormalizeZipPath(e.FullName),
                    candidate,
                    StringComparison.OrdinalIgnoreCase)
                && !NormalizeZipPath(e.FullName).EndsWith('/'));
            if (found is not null)
                return found;
        }

        return null;
    }

    private static bool TryGetDependency(
        IReadOnlyDictionary<string, string> dependencies,
        string key,
        out string value)
    {
        value = "";
        foreach (var pair in dependencies)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(pair.Value))
                return false;
            value = pair.Value.Trim();
            return true;
        }

        return false;
    }

    private static string BuildConfirmableSummary(
        string packName,
        string? versionId,
        string? summary,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        int? javaMajor,
        int fileCount,
        int serverRequired,
        int serverOptional,
        int clientOnly,
        int unclear,
        IReadOnlyList<string> packDeclaredPaths,
        IReadOnlyList<string> overrideListPaths,
        IReadOnlyList<string> inJarSkipPaths,
        IReadOnlyList<string> unclearPaths,
        bool hasOverrides,
        bool hasServerOverrides,
        bool hasClientOverrides,
        IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.Append("Pack: ").Append(packName);
        if (!string.IsNullOrEmpty(versionId))
            sb.Append(" (").Append(versionId).Append(')');
        sb.AppendLine();
        if (!string.IsNullOrEmpty(summary))
            sb.Append("Summary: ").Append(summary).AppendLine();
        sb.Append("Minecraft: ").AppendLine(minecraftVersion);
        sb.Append("Loader: ").Append(loader).Append(' ').AppendLine(loaderVersion);
        sb.Append("Required Java: ").AppendLine(javaMajor?.ToString() ?? "unknown");
        sb.Append("Files in pack: ").Append(fileCount).AppendLine();
        sb.Append("  Server-side: ").Append(serverRequired + serverOptional)
            .Append(" (").Append(serverRequired).Append(" required, ")
            .Append(serverOptional).AppendLine(" optional)");
        sb.Append("  Client-only (not installed on the server): ").AppendLine(clientOnly.ToString());
        sb.Append("    Pack-declared: ").AppendLine(packDeclaredPaths.Count.ToString());
        sb.Append("    Override list: ").AppendLine(overrideListPaths.Count.ToString());
        sb.Append("    In-jar metadata: ").AppendLine(inJarSkipPaths.Count.ToString());
        sb.Append("  Side unclear: ").Append(unclear);
        if (unclear > 0)
            sb.Append(" — do not install until reviewed");
        sb.AppendLine();

        if (packDeclaredPaths.Count > 0)
        {
            sb.AppendLine("Pack-declared client-only files:");
            foreach (var p in packDeclaredPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (overrideListPaths.Count > 0)
        {
            sb.AppendLine("Override-list skipped files:");
            foreach (var p in overrideListPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (inJarSkipPaths.Count > 0)
        {
            sb.AppendLine("In-jar client-only files:");
            foreach (var p in inJarSkipPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (unclearPaths.Count > 0)
        {
            sb.AppendLine("Unclear side (missing or unknown env.server):");
            foreach (var p in unclearPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (hasOverrides || hasServerOverrides || hasClientOverrides)
        {
            sb.Append("Overrides: ");
            var parts = new List<string>();
            if (hasOverrides)
                parts.Add("overrides/ (client+server)");
            if (hasServerOverrides)
                parts.Add("server-overrides/");
            if (hasClientOverrides)
                parts.Add("client-overrides/ (skipped on the server)");
            sb.AppendLine(string.Join("; ", parts));
        }

        if (warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in warnings)
                sb.Append("  ").AppendLine(w);
        }

        return sb.ToString().TrimEnd();
    }
}

internal sealed class MrpackIndexDocument
{
    public int FormatVersion { get; set; }

    public string? Game { get; set; }

    public string? VersionId { get; set; }

    public string? Name { get; set; }

    public string? Summary { get; set; }

    public Dictionary<string, string>? Dependencies { get; set; }

    public List<MrpackIndexFile>? Files { get; set; }
}

internal sealed class MrpackIndexFile
{
    public string? Path { get; set; }

    public MrpackFileEnv? Env { get; set; }

    public List<string>? Downloads { get; set; }

    public Dictionary<string, string>? Hashes { get; set; }

    public long? FileSize { get; set; }
}

internal sealed class MrpackFileEnv
{
    public string? Client { get; set; }

    public string? Server { get; set; }
}
