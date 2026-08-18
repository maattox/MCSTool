using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Parses a user-supplied generic server-pack zip locally (blueprint §24 / §2.4).
/// No catalog/search HTTP. Client-only jars are detected from in-jar metadata
/// when present; raw client packs are refused rather than heuristic-stripped.
/// </summary>
public static class ManualServerPackAnalyzer
{
    public const string ClientPackRefusal =
        "This looks like a client pack. If a server-pack download is available for it, "
        + "please upload that instead. This product will not silently strip client-only "
        + "mods from a launcher zip using a low-confidence heuristic.";

    public const string MrpackRefusal =
        "This archive is a Modrinth .mrpack (modrinth.index.json). Use the .mrpack import, "
        + "not the generic zip adapter.";

    public const string CurseForgeClientRefusal =
        "This looks like a CurseForge client export (manifest file IDs, no server jars). "
        + "If a Server Files zip is available for this pack, upload that instead. "
        + "Do not guess client-only mods from a client export.";

    public const string UnknownRefusal =
        "This zip does not look like a server pack (need a mods/ folder with jars, "
        + "or a Server Files zip that already contains libraries/ and the loader). "
        + "If this is a client pack, upload the server-pack download instead.";

    public const int MaxJarPeekBytes = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> KnownRootSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "mods", "config", "libraries", "defaultconfigs", "kubejs", "scripts",
        "world", "worlds", "datapacks", "overrides", "server-overrides",
        "resourcepacks", "shaderpacks", "screenshots", "saves",
    };

    public static ServiceResult<ManualServerPackAnalysis> AnalyzeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ServiceResult<ManualServerPackAnalysis>.Fail("No zip path was provided.");
        if (!File.Exists(path))
            return ServiceResult<ManualServerPackAnalysis>.Fail($"File not found: {path}");

        try
        {
            using var stream = File.OpenRead(path);
            return AnalyzeZip(stream, Path.GetFileName(path));
        }
        catch (UnauthorizedAccessException ex)
        {
            return ServiceResult<ManualServerPackAnalysis>.Fail($"Cannot read zip: {ex.Message}");
        }
        catch (IOException ex)
        {
            return ServiceResult<ManualServerPackAnalysis>.Fail($"Cannot read zip: {ex.Message}");
        }
    }

    public static ServiceResult<ManualServerPackAnalysis> AnalyzeZip(Stream zipStream, string? sourceName = null)
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
            return ServiceResult<ManualServerPackAnalysis>.Fail(
                $"{label} is not a valid ZIP archive.");
        }
        catch (NotSupportedException)
        {
            return ServiceResult<ManualServerPackAnalysis>.Fail("This archive uses an unsupported ZIP feature.");
        }

        using (zip)
            return AnalyzeArchive(zip, sourceName);
    }

    internal static ServiceResult<ManualServerPackAnalysis> AnalyzeArchive(ZipArchive zip, string? sourceName)
    {
        var rawNames = zip.Entries
            .Select(e => MrpackAnalyzer.NormalizeZipPath(e.FullName))
            .Where(n => n.Length > 0 && !ShouldIgnoreEntry(n))
            .ToList();

        var wrapper = DetectWrapperPrefix(rawNames);
        var names = wrapper is null
            ? rawNames
            : rawNames
                .Select(n => n.StartsWith(wrapper, StringComparison.OrdinalIgnoreCase) ? n[wrapper.Length..] : n)
                .Where(n => n.Length > 0)
                .ToList();

        var warnings = new List<string>();
        if (wrapper is not null)
            warnings.Add($"Stripped wrapper folder '{wrapper.TrimEnd('/')}/'.");

        var hasMrpackIndex = names.Any(n =>
            string.Equals(n, MrpackAnalyzer.IndexEntryName, StringComparison.OrdinalIgnoreCase));
        if (hasMrpackIndex)
            return OkRefused(ManualServerPackKind.Mrpack, MrpackRefusal, sourceName, wrapper, names, warnings);

        var hasOptions = names.Any(n => IsRootFile(n, "options.txt") || IsRootFile(n, "optionsof.txt"));
        var hasShaders = HasPrefix(names, "shaderpacks/");
        var hasSaves = HasPrefix(names, "saves/");
        var hasResourcepacks = HasPrefix(names, "resourcepacks/");
        var hasLibraries = HasPrefix(names, "libraries/");
        var hasRunSh = names.Any(n =>
            IsRootFile(n, "run.sh") || IsRootFile(n, "start.sh") || IsRootFile(n, "startserver.sh"));
        var hasInstallerJar = names.Any(IsRootInstallerJar);
        var modJars = names
            .Where(n => n.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                        && n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                        && !n.EndsWith('/'))
            .ToList();
        var looksLikeLauncher = hasOptions
            || (hasShaders && hasSaves && hasResourcepacks);
        if (looksLikeLauncher && !hasLibraries && !hasRunSh && !hasInstallerJar)
            return OkRefused(ManualServerPackKind.ClientPack, ClientPackRefusal, sourceName, wrapper, names, warnings);

        CurseForgeManifestDocument? cfManifest = null;
        var manifestEntry = zip.Entries.FirstOrDefault(e =>
        {
            var n = StripWrapper(MrpackAnalyzer.NormalizeZipPath(e.FullName), wrapper);
            return string.Equals(n, "manifest.json", StringComparison.OrdinalIgnoreCase);
        });
        if (manifestEntry is not null)
        {
            try
            {
                using var reader = new StreamReader(
                    manifestEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                cfManifest = JsonSerializer.Deserialize<CurseForgeManifestDocument>(reader.ReadToEnd(), JsonOptions);
            }
            catch (JsonException ex)
            {
                warnings.Add($"manifest.json is not valid JSON ({ex.Message}). Ignoring it.");
            }
        }

        var cfFiles = cfManifest?.Files ?? [];
        var looksLikeCfExport = cfManifest is not null
            && string.Equals(cfManifest.ManifestType, "minecraftModpack", StringComparison.OrdinalIgnoreCase)
            && cfFiles.Count > 0;
        if (looksLikeCfExport && modJars.Count == 0 && !hasLibraries && !hasInstallerJar)
        {
            return OkRefused(
                ManualServerPackKind.CurseForgeClientExport,
                CurseForgeClientRefusal,
                sourceName,
                wrapper,
                names,
                warnings,
                cfManifest);
        }

        var kind = ManualServerPackKind.Unknown;
        if (looksLikeCfExport && (hasLibraries || hasInstallerJar || modJars.Count > 0))
            kind = ManualServerPackKind.CurseForgeServerFiles;
        else if (modJars.Count > 0 || hasLibraries || hasInstallerJar)
            kind = ManualServerPackKind.UnstructuredServer;

        if (kind is ManualServerPackKind.Unknown)
            return OkRefused(ManualServerPackKind.Unknown, UnknownRefusal, sourceName, wrapper, names, warnings);

        var serverSide = new List<string>();
        var clientOnly = new List<string>();
        var unclear = new List<string>();

        foreach (var entry in zip.Entries)
        {
            var raw = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (raw.Length == 0 || ShouldIgnoreEntry(raw) || raw.EndsWith('/'))
                continue;
            var relative = StripWrapper(raw, wrapper);
            if (relative.Length == 0)
                continue;
            if (!relative.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                || !relative.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;

            var peek = PeekJarEnvironment(entry);
            if (peek.Environment == "client")
            {
                clientOnly.Add(relative);
                continue;
            }

            if (peek.HadMetadata)
            {
                serverSide.Add(relative);
                continue;
            }

            unclear.Add(relative);
            serverSide.Add(relative);
        }

        if (clientOnly.Count > 0)
        {
            warnings.Add(
                $"{clientOnly.Count} jar(s) tagged client-only in fabric/quilt/Forge metadata will not be installed.");
        }

        if (unclear.Count > 0)
        {
            warnings.Add(
                $"{unclear.Count} jar(s) have no in-jar side metadata; kept (server pack assumed). "
                + "This is not a Modrinth env.server strip.");
        }

        var packName = !string.IsNullOrWhiteSpace(cfManifest?.Name)
            ? cfManifest!.Name!.Trim()
            : GuessPackName(sourceName);
        var versionId = string.IsNullOrWhiteSpace(cfManifest?.Version) ? null : cfManifest!.Version!.Trim();
        var (loader, loaderVersion) = DetectLoader(names, cfManifest);
        var minecraft = (cfManifest?.Minecraft?.Version ?? "").Trim();
        if (minecraft.Length == 0)
            minecraft = "(unknown)";
        int? javaMajor = null;
        if (minecraft != "(unknown)" && MinecraftJavaFloor.TryGet(minecraft, out var mappedJava))
            javaMajor = mappedJava;
        else if (minecraft != "(unknown)")
            warnings.Add($"Could not map Minecraft {minecraft} to a Java major (blueprint §9.1).");
        else
            warnings.Add("Minecraft version is not declared in this zip (no CurseForge manifest).");

        if (loader == "unknown")
        {
            warnings.Add(
                "Loader not found in this zip. A documented mods/+config/ layout still installs; "
                + "install the matching loader separately if the pack does not already include it.");
        }

        var fileCount = names.Count(n => !n.EndsWith('/'));
        var canInstall = true;
        var summary = BuildConfirmableSummary(
            kind,
            packName,
            versionId,
            minecraft,
            loader,
            loaderVersion,
            javaMajor,
            wrapper,
            fileCount,
            serverSide.Count,
            clientOnly.Count,
            unclear.Count,
            clientOnly,
            warnings,
            canInstall: true,
            refusal: null);

        return ServiceResult<ManualServerPackAnalysis>.Ok(new ManualServerPackAnalysis(
            kind,
            canInstall,
            null,
            packName,
            versionId,
            minecraft,
            loader,
            loaderVersion,
            javaMajor,
            wrapper,
            fileCount,
            serverSide.Count,
            clientOnly.Count,
            unclear.Count,
            serverSide,
            clientOnly,
            unclear,
            warnings,
            summary));
    }

    internal static bool ShouldIgnoreEntry(string normalizedPath)
    {
        if (normalizedPath.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "__MACOSX", StringComparison.OrdinalIgnoreCase))
            return true;
        var leaf = normalizedPath.Split('/')[^1];
        return string.Equals(leaf, ".DS_Store", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? DetectWrapperPrefix(IReadOnlyList<string> names)
    {
        var segments = names
            .Select(n => n.Split('/', 2)[0])
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (segments.Count != 1)
            return null;
        var only = segments[0];
        if (KnownRootSegments.Contains(only))
            return null;
        if (only.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            || only.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || only.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
            || only.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || only.EndsWith(".properties", StringComparison.OrdinalIgnoreCase))
            return null;
        return only + "/";
    }

    internal static string StripWrapper(string normalizedPath, string? wrapper)
    {
        if (wrapper is null)
            return normalizedPath;
        if (normalizedPath.StartsWith(wrapper, StringComparison.OrdinalIgnoreCase))
            return normalizedPath[wrapper.Length..];
        return normalizedPath;
    }

    internal static JarEnvironmentPeek PeekJarEnvironment(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0)
            return JarEnvironmentPeek.None;
        if (entry.Length > MaxJarPeekBytes)
            return JarEnvironmentPeek.None;

        try
        {
            using var owned = new MemoryStream((int)Math.Min(entry.Length, MaxJarPeekBytes));
            using (var input = entry.Open())
                input.CopyTo(owned);
            owned.Position = 0;
            return PeekJarEnvironment(owned);
        }
        catch (InvalidDataException)
        {
            return JarEnvironmentPeek.None;
        }
        catch (IOException)
        {
            return JarEnvironmentPeek.None;
        }
    }

    internal static JarEnvironmentPeek PeekJarEnvironment(Stream jarStream)
    {
        ZipArchive jar;
        try
        {
            jar = new ZipArchive(jarStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return JarEnvironmentPeek.None;
        }
        catch (NotSupportedException)
        {
            return JarEnvironmentPeek.None;
        }

        using (jar)
        {
            var fabric = jar.Entries.FirstOrDefault(e =>
                string.Equals(
                    MrpackAnalyzer.NormalizeZipPath(e.FullName),
                    "fabric.mod.json",
                    StringComparison.OrdinalIgnoreCase));
            if (fabric is not null && TryReadFabricEnvironment(fabric, out var fabricEnv))
                return new JarEnvironmentPeek(true, fabricEnv);

            var quilt = jar.Entries.FirstOrDefault(e =>
                string.Equals(
                    MrpackAnalyzer.NormalizeZipPath(e.FullName),
                    "quilt.mod.json",
                    StringComparison.OrdinalIgnoreCase));
            if (quilt is not null && TryReadQuiltEnvironment(quilt, out var quiltEnv))
                return new JarEnvironmentPeek(true, quiltEnv);

            var toml = jar.Entries.FirstOrDefault(e =>
            {
                var n = MrpackAnalyzer.NormalizeZipPath(e.FullName);
                return n.Equals("META-INF/mods.toml", StringComparison.OrdinalIgnoreCase)
                    || n.Equals("META-INF/neoforge.mods.toml", StringComparison.OrdinalIgnoreCase);
            });
            if (toml is not null && TryReadTomlModsSide(toml, out var tomlEnv))
                return new JarEnvironmentPeek(true, tomlEnv);
        }

        return JarEnvironmentPeek.None;
    }

    internal readonly record struct JarEnvironmentPeek(bool HadMetadata, string Environment)
    {
        public static JarEnvironmentPeek None => new(false, "*");
    }

    private static ServiceResult<ManualServerPackAnalysis> OkRefused(
        ManualServerPackKind kind,
        string refusal,
        string? sourceName,
        string? wrapper,
        IReadOnlyList<string> names,
        List<string> warnings,
        CurseForgeManifestDocument? cfManifest = null)
    {
        warnings.Add(refusal);
        var packName = !string.IsNullOrWhiteSpace(cfManifest?.Name)
            ? cfManifest!.Name!.Trim()
            : GuessPackName(sourceName);
        var versionId = string.IsNullOrWhiteSpace(cfManifest?.Version) ? null : cfManifest!.Version!.Trim();
        var (loader, loaderVersion) = DetectLoader(names, cfManifest);
        var minecraft = (cfManifest?.Minecraft?.Version ?? "").Trim();
        if (minecraft.Length == 0)
            minecraft = "(unknown)";
        int? javaMajor = null;
        if (minecraft != "(unknown)" && MinecraftJavaFloor.TryGet(minecraft, out var mapped))
            javaMajor = mapped;
        var fileCount = names.Count(n => !n.EndsWith('/'));
        var summary = BuildConfirmableSummary(
            kind, packName, versionId, minecraft, loader, loaderVersion, javaMajor, wrapper,
            fileCount, 0, 0, 0, [], warnings, canInstall: false, refusal);

        return ServiceResult<ManualServerPackAnalysis>.Ok(new ManualServerPackAnalysis(
            kind,
            false,
            refusal,
            packName,
            versionId,
            minecraft,
            loader,
            loaderVersion,
            javaMajor,
            wrapper,
            fileCount,
            0,
            0,
            0,
            [],
            [],
            [],
            warnings,
            summary));
    }

    private static bool TryReadFabricEnvironment(ZipArchiveEntry entry, out string environment)
    {
        environment = "*";
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var allClient = true;
                var any = false;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    any = true;
                    var env = ReadStringProperty(item, "environment");
                    if (!env.Equals("client", StringComparison.OrdinalIgnoreCase))
                        allClient = false;
                }

                if (!any)
                    return false;
                environment = allClient ? "client" : "*";
                return true;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var single = ReadStringProperty(doc.RootElement, "environment");
            environment = string.IsNullOrEmpty(single) ? "*" : single.Trim().ToLowerInvariant();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReadQuiltEnvironment(ZipArchiveEntry entry, out string environment)
    {
        environment = "*";
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (doc.RootElement.TryGetProperty("quilt_loader", out var loader)
                && loader.ValueKind == JsonValueKind.Object)
            {
                var env = ReadStringProperty(loader, "environment");
                environment = string.IsNullOrEmpty(env) ? "*" : env.Trim().ToLowerInvariant();
                return true;
            }

            var top = ReadStringProperty(doc.RootElement, "environment");
            if (string.IsNullOrEmpty(top))
                return false;
            environment = top.Trim().ToLowerInvariant();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryReadTomlModsSide(ZipArchiveEntry entry, out string environment)
    {
        environment = "*";
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var toml = reader.ReadToEnd();
            string? table = null;
            foreach (var raw in toml.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (line.StartsWith('['))
                {
                    table = line;
                    continue;
                }

                if (table is null)
                    continue;
                var isModsTable = table.Equals("[[mods]]", StringComparison.OrdinalIgnoreCase)
                    || table.Equals("[mods]", StringComparison.OrdinalIgnoreCase);
                if (!isModsTable)
                    continue;
                if (!TryParseTomlStringAssignment(line, "side", out var side))
                    continue;
                environment = side.Equals("CLIENT", StringComparison.OrdinalIgnoreCase) ? "client" : "*";
                return true;
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryParseTomlStringAssignment(string line, string key, out string value)
    {
        value = "";
        var eq = line.IndexOf('=');
        if (eq <= 0)
            return false;
        if (!line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;
        var rhs = line[(eq + 1)..].Trim().Trim('"').Trim('\'');
        if (rhs.Length == 0)
            return false;
        value = rhs;
        return true;
    }

    private static string ReadStringProperty(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
        }

        return "";
    }

    private static (string Loader, string Version) DetectLoader(
        IReadOnlyList<string> names,
        CurseForgeManifestDocument? manifest)
    {
        var primary = manifest?.Minecraft?.ModLoaders?.FirstOrDefault(l => l.Primary)
            ?? manifest?.Minecraft?.ModLoaders?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(primary?.Id)
            && TrySplitLoaderId(primary.Id!, out var fromManifest, out var fromManifestVer))
            return (fromManifest, fromManifestVer);

        if (names.Any(n => n.StartsWith("libraries/net/neoforged/", StringComparison.OrdinalIgnoreCase)
                           || n.StartsWith("libraries/net/neoforge/", StringComparison.OrdinalIgnoreCase)
                           || IsRootFile(n, prefix: "neoforge-", suffix: ".jar")))
            return (MrpackAnalyzer.LoaderNeoForge, "");
        if (names.Any(n => n.StartsWith("libraries/net/minecraftforge/", StringComparison.OrdinalIgnoreCase)
                           || IsRootFile(n, prefix: "forge-", suffix: ".jar")))
            return (MrpackAnalyzer.LoaderForge, "");
        if (names.Any(n => IsRootFile(n, prefix: "fabric-server", suffix: ".jar")
                           || IsRootFile(n, prefix: "fabric-server-launch", suffix: ".jar")))
            return (MrpackAnalyzer.LoaderFabric, "");
        if (names.Any(n => IsRootFile(n, prefix: "quilt-server", suffix: ".jar")))
            return (MrpackAnalyzer.LoaderQuilt, "");
        return ("unknown", "");
    }

    private static bool TrySplitLoaderId(string id, out string loader, out string version)
    {
        loader = "unknown";
        version = "";
        var trimmed = id.Trim();
        var known = new[]
        {
            ("neoforge", MrpackAnalyzer.LoaderNeoForge),
            ("fabric", MrpackAnalyzer.LoaderFabric),
            ("quilt", MrpackAnalyzer.LoaderQuilt),
            ("forge", MrpackAnalyzer.LoaderForge),
        };
        foreach (var (prefix, loaderId) in known)
        {
            if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            loader = loaderId;
            if (trimmed.Length > prefix.Length && trimmed[prefix.Length] == '-')
                version = trimmed[(prefix.Length + 1)..];
            return true;
        }

        return false;
    }

    private static bool HasPrefix(IReadOnlyList<string> names, string prefix) =>
        names.Any(n => n.Equals(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                       || n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsRootFile(string name, string fileName) =>
        string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase);

    private static bool IsRootFile(string name, string prefix, string suffix) =>
        !name.Contains('/')
        && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsRootInstallerJar(string name)
    {
        var leaf = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
        if (name.Contains('/'))
            return false;
        return leaf.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && leaf.Contains("installer", StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessPackName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return "(unnamed pack)";
        var name = Path.GetFileNameWithoutExtension(sourceName).Trim();
        return string.IsNullOrEmpty(name) ? "(unnamed pack)" : name;
    }

    private static string BuildConfirmableSummary(
        ManualServerPackKind kind,
        string packName,
        string? versionId,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        int? javaMajor,
        string? wrapper,
        int fileCount,
        int serverSide,
        int clientOnly,
        int unclear,
        IReadOnlyList<string> clientOnlyPaths,
        IReadOnlyList<string> warnings,
        bool canInstall,
        string? refusal)
    {
        var sb = new StringBuilder();
        sb.Append("Pack: ").Append(packName);
        if (!string.IsNullOrEmpty(versionId))
            sb.Append(" (").Append(versionId).Append(')');
        sb.AppendLine();
        sb.Append("Kind: ").AppendLine(kind.ToString());
        sb.Append("Minecraft: ").AppendLine(minecraftVersion);
        sb.Append("Loader: ").Append(loader);
        if (!string.IsNullOrEmpty(loaderVersion))
            sb.Append(' ').Append(loaderVersion);
        sb.AppendLine();
        sb.Append("Required Java: ").AppendLine(javaMajor?.ToString() ?? "unknown");
        if (wrapper is not null)
            sb.Append("Wrapper folder: ").AppendLine(wrapper.TrimEnd('/'));
        sb.Append("Files in zip: ").Append(fileCount).AppendLine();
        if (canInstall)
        {
            sb.Append("  Server-side jars: ").Append(serverSide).AppendLine();
            sb.Append("  Client-only (in-jar metadata; not installed): ").AppendLine(clientOnly.ToString());
            sb.Append("  No side metadata (kept): ").AppendLine(unclear.ToString());
        }
        else
        {
            sb.AppendLine("Will not install this zip as a server pack.");
        }

        if (clientOnlyPaths.Count > 0)
        {
            sb.AppendLine("Client-only jars:");
            foreach (var p in clientOnlyPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (!string.IsNullOrEmpty(refusal))
            sb.Append("Reason: ").AppendLine(refusal);

        if (warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in warnings)
                sb.Append("  ").AppendLine(w);
        }

        return sb.ToString().TrimEnd();
    }
}

internal sealed class CurseForgeManifestDocument
{
    public string? Name { get; set; }

    public string? Version { get; set; }

    public string? ManifestType { get; set; }

    public int ManifestVersion { get; set; }

    public CurseForgeMinecraftSection? Minecraft { get; set; }

    public List<CurseForgeManifestFile>? Files { get; set; }

    public string? Overrides { get; set; }
}

internal sealed class CurseForgeMinecraftSection
{
    public string? Version { get; set; }

    public List<CurseForgeModLoader>? ModLoaders { get; set; }
}

internal sealed class CurseForgeModLoader
{
    public string? Id { get; set; }

    public bool Primary { get; set; }
}

internal sealed class CurseForgeManifestFile
{
    public int ProjectID { get; set; }

    public int FileID { get; set; }

    public bool Required { get; set; }
}
