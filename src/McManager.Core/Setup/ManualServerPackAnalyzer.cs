using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Parses a user-supplied generic server-pack zip locally (blueprint §24 / §2.4).
/// No catalog/search HTTP. Client-only jars are detected from in-jar metadata
/// and the CurseForge itzg/product exclude list. Jar-root zips install as
/// <c>mods/</c>. Raw client packs and jar-less / mixed-ID CurseForge exports
/// are refused rather than heuristic-stripped.
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

    public const string CurseForgeIncompleteRefusal =
        "This CurseForge zip lists mods in the manifest but does not include the pre-downloaded "
        + "mod jars (libraries or an installer alone is not enough). Download the pack's "
        + "Server Files zip, or a filled zip that already contains the mods/ jars, and upload "
        + "that instead. This app cannot download missing jars from CurseForge.";

    public const string UnknownRefusal =
        "This zip does not look like a server pack (need a mods/ folder with jars, "
        + "jars at the archive root, or a Server Files zip that already contains libraries/ and the loader). "
        + "If this is a client pack, upload the server-pack download instead.";

    public const string CurseForgeMixedRefusal =
        "This CurseForge zip includes some mod jars but the manifest still lists files that are not in the archive. "
        + "Upload a complete Server Files zip, or a filled zip that already contains every listed jar. "
        + "This app cannot download missing jars from CurseForge.";

    public const int MaxJarPeekBytes = 32 * 1024 * 1024;
    public const int ListedFileIdCap = 20;

    private static readonly Lazy<ExcludeIncludeMatcher> DefaultMatcher =
        new(() => ExcludeIncludeMatcher.ForCurseForge());

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

    public static ServiceResult<ManualServerPackAnalysis> AnalyzeFile(
        string path,
        ExcludeIncludeMatcher? matcher = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ServiceResult<ManualServerPackAnalysis>.Fail("No zip path was provided.");
        if (!File.Exists(path))
            return ServiceResult<ManualServerPackAnalysis>.Fail($"File not found: {path}");

        try
        {
            using var stream = File.OpenRead(path);
            return AnalyzeZip(stream, Path.GetFileName(path), matcher);
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

    public static ServiceResult<ManualServerPackAnalysis> AnalyzeZip(
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
            return ServiceResult<ManualServerPackAnalysis>.Fail(
                $"{label} is not a valid ZIP archive.");
        }
        catch (NotSupportedException)
        {
            return ServiceResult<ManualServerPackAnalysis>.Fail("This archive uses an unsupported ZIP feature.");
        }

        using (zip)
            return AnalyzeArchive(zip, sourceName, matcher);
    }

    internal static ServiceResult<ManualServerPackAnalysis> AnalyzeArchive(
        ZipArchive zip,
        string? sourceName,
        ExcludeIncludeMatcher? matcher = null)
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

        var hasSidecar = names.Any(n =>
            string.Equals(n, DerivedPackIdentity.SidecarEntryName, StringComparison.OrdinalIgnoreCase));
        DerivedPackSidecar? sidecar = hasSidecar ? TryReadSidecar(zip, wrapper) : null;
        if (hasSidecar && sidecar is null)
            warnings.Add($"{DerivedPackIdentity.SidecarEntryName} is present but could not be read.");

        var hasMrpackIndex = names.Any(n =>
            string.Equals(n, MrpackAnalyzer.IndexEntryName, StringComparison.OrdinalIgnoreCase));
        if (hasMrpackIndex && !hasSidecar)
            return OkRefused(ManualServerPackKind.Mrpack, MrpackRefusal, sourceName, wrapper, names, warnings);

        var hasOptions = names.Any(n => IsRootFile(n, "options.txt") || IsRootFile(n, "optionsof.txt"));
        var hasShaders = HasPrefix(names, "shaderpacks/");
        var hasSaves = HasPrefix(names, "saves/");
        var hasResourcepacks = HasPrefix(names, "resourcepacks/");
        var hasLibraries = HasPrefix(names, "libraries/");
        var hasRunSh = names.Any(n =>
            IsRootFile(n, "run.sh") || IsRootFile(n, "start.sh") || IsRootFile(n, "startserver.sh"));
        var hasInstallerJar = names.Any(IsRootInstallerJar);
        var mapRootJarsToMods = !hasSidecar && LooksLikeJarRootPack(names);
        var modJars = names.Where(ManualPackFileFilter.IsModJarPath).ToList();
        if (hasSidecar)
            modJars = names.Where(IsDerivedModJarPath).ToList();
        if (modJars.Count == 0 && mapRootJarsToMods)
            modJars = names.Where(ManualPackFileFilter.IsRootModJar).ToList();
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
        if (looksLikeCfExport && modJars.Count == 0)
        {
            // Client export (IDs only) and Server Files / installer zips that omit mods/*.jar
            // both cannot be filled in without the CurseForge API (v1 Step 4.12 deferred).
            var refusal = hasLibraries || hasInstallerJar
                ? CurseForgeIncompleteRefusal
                : CurseForgeClientRefusal;
            AddListedFileIdWarning(warnings, cfFiles);
            return OkRefused(
                ManualServerPackKind.CurseForgeClientExport,
                refusal,
                sourceName,
                wrapper,
                names,
                warnings,
                cfManifest);
        }

        if (looksLikeCfExport && cfFiles.Count > modJars.Count)
        {
            AddListedFileIdWarning(warnings, cfFiles);
            warnings.Add(
                $"Manifest lists {cfFiles.Count} file(s) and the zip has {modJars.Count} mod jar(s).");
            return OkRefused(
                ManualServerPackKind.CurseForgeClientExport,
                CurseForgeMixedRefusal,
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

        var lists = matcher ?? DefaultMatcher.Value;
        var packName = !string.IsNullOrWhiteSpace(cfManifest?.Name)
            ? cfManifest!.Name!.Trim()
            : GuessPackName(sourceName);
        var versionId = string.IsNullOrWhiteSpace(cfManifest?.Version) ? null : cfManifest!.Version!.Trim();
        var packSlug = MrpackFileFilter.ResolvePackSlug(lists, packName, versionId, sourceName);

        var forceIncluded = new List<string>();
        var jarRecords = new List<PackJarRecord>();
        string? peekedLoader = null;
        string? peekedMinecraft = null;

        foreach (var entry in zip.Entries)
        {
            var raw = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (raw.Length == 0 || ShouldIgnoreEntry(raw) || raw.EndsWith('/'))
                continue;
            var relative = StripWrapper(raw, wrapper);
            if (relative.Length == 0)
                continue;
            var isModJar = IsDerivedModJarPath(relative)
                || ManualPackFileFilter.IsModJarPath(relative)
                || (mapRootJarsToMods && ManualPackFileFilter.IsRootModJar(relative));
            if (!isModJar)
                continue;

            var matchPath = ResolveModMatchPath(relative, mapRootJarsToMods);
            var peek = PeekJarEnvironment(entry);
            if (!string.IsNullOrEmpty(peek.Loader) && peekedLoader is null)
                peekedLoader = peek.Loader;
            if (!string.IsNullOrEmpty(peek.MinecraftVersion) && peekedMinecraft is null)
                peekedMinecraft = peek.MinecraftVersion;

            var match = lists.Match(packSlug, matchPath);
            var action = ManualPackFileFilter.Decide(peek.Environment, match);
            var autoSkip = action switch
            {
                ManualPackFileFilter.Action.SkipInJarMetadata => PackFileSkipReason.InJarMetadata,
                ManualPackFileFilter.Action.SkipOverrideList => PackFileSkipReason.OverrideList,
                _ => PackFileSkipReason.None,
            };
            if (match.Keep && peek.Environment.Equals("client", StringComparison.OrdinalIgnoreCase))
                forceIncluded.Add(relative);

            jarRecords.Add(new PackJarRecord(
                relative,
                peek.AllProvidedModIds,
                peek.AllRequiredModIds,
                unclearSide: autoSkip == PackFileSkipReason.None && !peek.HadMetadata,
                forceIncluded: match.Keep && peek.Environment.Equals("client", StringComparison.OrdinalIgnoreCase),
                automaticSkipReason: autoSkip,
                skipDetail: autoSkip == PackFileSkipReason.OverrideList
                    ? "exclude list"
                    : autoSkip == PackFileSkipReason.InJarMetadata
                        ? "in-jar client"
                        : null));
        }

        var classified = PackDependencyFreeze.Classify(jarRecords);
        var serverSide = classified.ServerSidePaths.ToList();
        var clientOnly = classified.ClientOnlyPaths.ToList();
        var inJarSkip = classified.InJarMetadataSkipPaths.ToList();
        var overrideListSkip = classified.OverrideListSkipPaths.ToList();
        var unclear = classified.UnclearSidePaths.ToList();

        if (inJarSkip.Count > 0)
        {
            warnings.Add(
                $"{inJarSkip.Count} jar(s) detected as client-only from in-jar metadata will not be installed.");
        }

        if (overrideListSkip.Count > 0)
        {
            warnings.Add(
                $"{overrideListSkip.Count} jar(s) skipped by the CurseForge exclude list (known client-only).");
        }

        if (unclear.Count > 0)
        {
            warnings.Add(
                $"{unclear.Count} jar(s) have no in-jar side metadata. Review them below (default Keep).");
        }

        if (mapRootJarsToMods)
            warnings.Add("Archive has jars at the root (no mods/ folder); they will install into mods/.");

        var (loader, loaderVersion) = DetectLoader(names, cfManifest);
        if (loader == "unknown" && peekedLoader is not null)
            loader = peekedLoader;

        var minecraft = (cfManifest?.Minecraft?.Version ?? "").Trim();
        if (minecraft.Length == 0 && peekedMinecraft is not null)
            minecraft = peekedMinecraft;
        if (minecraft.Length == 0)
            minecraft = "(unknown)";
        int? javaMajor = null;
        if (minecraft != "(unknown)" && MinecraftJavaFloor.TryGet(minecraft, out var mappedJava))
            javaMajor = mappedJava;
        else if (minecraft != "(unknown)")
            warnings.Add($"Could not map Minecraft {minecraft} to a Java major (blueprint §9.1).");
        else
            warnings.Add("Minecraft version is not declared in this zip (no CurseForge manifest).");

        if (loader == "unknown" && !hasSidecar)
        {
            warnings.Add(
                "Loader not found in this zip. A documented mods/+config/ layout still installs; "
                + "install the matching loader separately if the pack does not already include it.");
        }

        var detectedMinecraft = minecraft;
        var detectedLoader = loader;
        if (sidecar is not null)
        {
            minecraft = sidecar.MinecraftVersion;
            loader = sidecar.Loader;
            loaderVersion = sidecar.LoaderVersion;
            javaMajor = sidecar.JavaMajor;
            if (!string.IsNullOrWhiteSpace(sidecar.DetectedMinecraftVersion))
                detectedMinecraft = sidecar.DetectedMinecraftVersion!;
            if (!string.IsNullOrWhiteSpace(sidecar.DetectedLoader))
                detectedLoader = sidecar.DetectedLoader!;
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
            inJarSkip,
            overrideListSkip,
            warnings,
            canInstall: true,
            refusal: null,
            mapRootJarsToMods);

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
            summary,
            mapRootJarsToMods,
            overrideListSkip.Count,
            inJarSkip.Count,
            overrideListSkip,
            inJarSkip,
            forceIncluded,
            detectedMinecraft,
            detectedLoader,
            hasSidecar,
            classified.Review,
            jarRecords,
            classified.FreezeBlockReason));
    }

    internal static bool IsDerivedModJarPath(string relative)
    {
        var n = (relative ?? "").Replace('\\', '/');
        return n.StartsWith("overrides/mods/", StringComparison.OrdinalIgnoreCase)
            && n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && !n.EndsWith('/');
    }

    private static string ResolveModMatchPath(string relative, bool mapRootJarsToMods)
    {
        if (IsDerivedModJarPath(relative))
            return "mods/" + relative["overrides/mods/".Length..];
        if (ManualPackFileFilter.IsModJarPath(relative))
            return relative;
        return "mods/" + relative;
    }

    private static DerivedPackSidecar? TryReadSidecar(ZipArchive zip, string? wrapper)
    {
        var entry = zip.Entries.FirstOrDefault(e =>
        {
            var n = StripWrapper(MrpackAnalyzer.NormalizeZipPath(e.FullName), wrapper);
            return string.Equals(n, DerivedPackIdentity.SidecarEntryName, StringComparison.OrdinalIgnoreCase);
        });
        if (entry is null)
            return null;

        try
        {
            using var reader = new StreamReader(
                entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return JsonSerializer.Deserialize<DerivedPackSidecar>(reader.ReadToEnd(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
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

    internal static InJarSideDetector.PeekResult PeekJarEnvironment(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0)
            return InJarSideDetector.PeekResult.None;
        if (entry.Length > MaxJarPeekBytes)
            return InJarSideDetector.PeekResult.None;

        try
        {
            using var owned = new MemoryStream((int)Math.Min(entry.Length, MaxJarPeekBytes));
            using (var input = entry.Open())
                input.CopyTo(owned);
            owned.Position = 0;
            return InJarSideDetector.Peek(owned);
        }
        catch (InvalidDataException)
        {
            return InJarSideDetector.PeekResult.None;
        }
        catch (IOException)
        {
            return InJarSideDetector.PeekResult.None;
        }
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
            fileCount, 0, 0, 0, [], [], warnings, canInstall: false, refusal);

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

    private static bool IsRootInstallerJar(string name) =>
        !name.Contains('/') && IsInstallerJarFileName(name);

    internal static bool IsInstallerJarFileName(string fileName)
    {
        var leaf = fileName.Contains('/') ? fileName[(fileName.LastIndexOf('/') + 1)..] : fileName;
        return leaf.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && leaf.Contains("installer", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool LooksLikeJarRootPack(IReadOnlyList<string> names)
    {
        var files = names.Where(n => n.Length > 0 && !n.EndsWith('/')).ToList();
        if (files.Count == 0)
            return false;
        if (files.Any(ManualPackFileFilter.IsModJarPath))
            return false;
        if (files.Any(n => n.Contains('/')))
            return false;
        if (!files.Any(ManualPackFileFilter.IsRootModJar))
            return false;

        foreach (var other in files)
        {
            if (ManualPackFileFilter.IsRootModJar(other) || IsRootInstallerJar(other) || IsJarRootCompanion(other))
                continue;
            return false;
        }

        return true;
    }

    internal static bool IsJarRootCompanion(string name)
    {
        if (name.Contains('/'))
            return false;
        return name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || name.Equals("changelog", StringComparison.OrdinalIgnoreCase)
            || name.Equals("readme", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddListedFileIdWarning(List<string> warnings, IReadOnlyList<CurseForgeManifestFile> files)
    {
        if (files.Count == 0)
            return;
        var shown = files.Take(ListedFileIdCap).Select(f => $"{f.ProjectID}:{f.FileID}");
        var text = string.Join(", ", shown);
        if (files.Count > ListedFileIdCap)
            text += $", … ({files.Count - ListedFileIdCap} more)";
        warnings.Add("Listed CurseForge file IDs (project:file): " + text + ".");
    }

    internal static bool TryExtractMinecraftVersion(string? raw, out string version) =>
        InJarSideDetector.TryExtractMinecraftVersion(raw, out version);

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
        IReadOnlyList<string> inJarSkipPaths,
        IReadOnlyList<string> overrideListSkipPaths,
        IReadOnlyList<string> warnings,
        bool canInstall,
        string? refusal,
        bool mapRootJarsToMods = false)
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
        if (mapRootJarsToMods)
            sb.AppendLine("Root jars install into mods/.");
        sb.Append("Files in zip: ").Append(fileCount).AppendLine();
        if (canInstall)
        {
            sb.Append("  Server-side jars: ").Append(serverSide).AppendLine();
            sb.Append("  Client-only (not installed on the server): ").AppendLine(clientOnly.ToString());
            sb.Append("    In-jar metadata: ").AppendLine(inJarSkipPaths.Count.ToString());
            sb.Append("    Override list: ").AppendLine(overrideListSkipPaths.Count.ToString());
            sb.Append("  No side metadata (kept): ").AppendLine(unclear.ToString());
        }
        else
        {
            sb.AppendLine("Will not install this zip as a server pack.");
        }

        if (inJarSkipPaths.Count > 0)
        {
            sb.AppendLine("In-jar client-only jars:");
            foreach (var p in inJarSkipPaths)
                sb.Append("  ").AppendLine(p);
        }

        if (overrideListSkipPaths.Count > 0)
        {
            sb.AppendLine("Override-list skipped jars:");
            foreach (var p in overrideListSkipPaths)
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
