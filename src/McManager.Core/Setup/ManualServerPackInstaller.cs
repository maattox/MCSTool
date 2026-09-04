using System.IO.Compression;
using System.Text;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Installs a user-supplied generic server-pack zip into a destination directory
/// (blueprint §24). Unzips documented layout; strips jars whose in-jar metadata
/// is client-only or whose name matches the CurseForge exclude list. Root-only
/// jar zips install into dest <c>mods/</c>. No catalog HTTP. Does not rewrite
/// <see cref="MrpackInstaller"/>.
/// </summary>
public static class ManualServerPackInstaller
{
    private static readonly string[] CopyPrefixes =
    [
        "mods/",
        "config/",
        "defaultconfigs/",
        "kubejs/",
        "scripts/",
        "libraries/",
        "world/",
        "worlds/",
        "datapacks/",
        "resourcepacks/",
    ];

    private static readonly string[] FlattenPrefixes =
    [
        "overrides/",
        "server-overrides/",
    ];

    private static readonly string[] SkipPrefixes =
    [
        "shaderpacks/",
        "screenshots/",
        "client-overrides/",
        "__macosx/",
        ".fabric/",
        ".connector/",
    ];

    private static readonly HashSet<string> RootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "run.sh", "run.bat", "start.sh", "start.bat",
        "startserver.sh", "startserver.bat",
        "unix_args.txt", "user_jvm_args.txt", "win_args.txt",
        "server.properties", "eula.txt", "manifest.json",
    };

    private static readonly HashSet<string> SkipRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "options.txt", "optionsof.txt", "optionsshaders.txt", ".ds_store",
    };

    public static ServiceResult<ManualServerPackInstallResult> Install(
        string zipPath,
        string destDirectory,
        string? retainDataDirectory,
        ExcludeIncludeMatcher? matcher = null)
    {
        matcher ??= ExcludeIncludeMatcher.ForCurseForge(
            retainDataDirectory,
            Layer2LocalOverlay.TryHashFile(zipPath));
        var analysisResult = ManualServerPackAnalyzer.AnalyzeFile(zipPath, matcher);
        if (!analysisResult.Succeeded)
            return ServiceResult<ManualServerPackInstallResult>.Fail(analysisResult.Error!);

        var analysis = analysisResult.Value!;
        if (!analysis.CanInstall)
        {
            return ServiceResult<ManualServerPackInstallResult>.Fail(
                analysis.RefusalReason ?? ManualServerPackAnalyzer.UnknownRefusal);
        }

        if (string.IsNullOrWhiteSpace(destDirectory))
            return ServiceResult<ManualServerPackInstallResult>.Fail("No install destination directory was provided.");

        try
        {
            Directory.CreateDirectory(destDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ServiceResult<ManualServerPackInstallResult>.Fail($"Cannot create destination: {ex.Message}");
        }

        try
        {
            using var stream = File.OpenRead(zipPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var installed = new List<string>();
            var skippedClientOnly = new List<string>(analysis.ClientOnlyPaths);
            var warnings = new List<string>(analysis.Warnings);
            var clientOnly = new HashSet<string>(analysis.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zip.Entries)
            {
                var raw = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
                if (raw.Length == 0 || ManualServerPackAnalyzer.ShouldIgnoreEntry(raw) || raw.EndsWith('/'))
                    continue;

                var relative = ManualServerPackAnalyzer.StripWrapper(raw, analysis.WrapperPrefix);
                if (relative.Length == 0)
                    continue;

                if (!TryMapInstallPath(relative, analysis.MapRootJarsToMods, out var destRelative))
                    continue;

                if (clientOnly.Contains(relative))
                    continue;

                var destPath = MrpackInstaller.ResolveUnderDest(destDirectory, destRelative);
                if (!destPath.Succeeded)
                    return ServiceResult<ManualServerPackInstallResult>.Fail(destPath.Error!);

                Directory.CreateDirectory(Path.GetDirectoryName(destPath.Value!)!);
                using (var input = entry.Open())
                using (var output = File.Create(destPath.Value!))
                    input.CopyTo(output);

                installed.Add(destRelative.Replace('\\', '/'));
            }

            string? retained = null;
            if (!string.IsNullOrWhiteSpace(retainDataDirectory))
            {
                var retain = ImportedPackArchiveStore.Retain(
                    zipPath,
                    analysis.PackName,
                    analysis.VersionId,
                    analysis.Loader,
                    analysis.MinecraftVersion,
                    retainDataDirectory);
                if (!retain.Succeeded)
                    return ServiceResult<ManualServerPackInstallResult>.Fail(retain.Error!);
                retained = retain.Value;
            }
            else
            {
                warnings.Add("Original zip was not copied into Manager local data (no data directory).");
            }

            var summary = BuildSummary(
                analysis,
                destDirectory,
                retained,
                installed,
                skippedClientOnly,
                warnings);

            return ServiceResult<ManualServerPackInstallResult>.Ok(new ManualServerPackInstallResult(
                analysis,
                Path.GetFullPath(destDirectory),
                retained,
                installed,
                skippedClientOnly,
                warnings,
                summary));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ServiceResult<ManualServerPackInstallResult>.Fail($"Cannot install zip: {ex.Message}");
        }
    }

    internal static bool TryMapInstallPath(string relative, out string destRelative) =>
        TryMapInstallPath(relative, mapRootJarsToMods: false, out destRelative);

    internal static bool TryMapInstallPath(string relative, bool mapRootJarsToMods, out string destRelative)
    {
        destRelative = relative;
        var normalized = relative.Replace('\\', '/');
        var lower = normalized.ToLowerInvariant();

        foreach (var skip in SkipPrefixes)
        {
            if (lower.StartsWith(skip, StringComparison.Ordinal))
                return false;
        }

        var leaf = normalized.Contains('/') ? normalized[(normalized.LastIndexOf('/') + 1)..] : normalized;
        if (SkipRootFiles.Contains(leaf))
            return false;

        foreach (var prefix in FlattenPrefixes)
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (normalized.Length <= prefix.Length)
                return false;
            destRelative = normalized[prefix.Length..];
            return destRelative.Length > 0;
        }

        foreach (var prefix in CopyPrefixes)
        {
            if (lower.Equals(prefix.TrimEnd('/'), StringComparison.Ordinal)
                || lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                destRelative = normalized;
                return true;
            }
        }

        if (!normalized.Contains('/') && RootFiles.Contains(normalized))
            return true;

        if (!normalized.Contains('/')
            && normalized.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            if (mapRootJarsToMods && ManualPackFileFilter.IsRootModJar(normalized))
            {
                destRelative = "mods/" + normalized;
                return true;
            }

            destRelative = normalized;
            return true;
        }

        return false;
    }

    private static string BuildSummary(
        ManualServerPackAnalysis analysis,
        string destDirectory,
        string? retainedArchivePath,
        IReadOnlyList<string> installed,
        IReadOnlyList<string> skippedClientOnly,
        IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine(analysis.ConfirmableSummary);
        sb.AppendLine();
        sb.Append("Installed into: ").AppendLine(Path.GetFullPath(destDirectory));
        sb.Append("Server-side files written: ").AppendLine(installed.Count.ToString());
        foreach (var p in installed)
            sb.Append("  ").AppendLine(p);
        sb.Append("Client-only skipped: ").AppendLine(skippedClientOnly.Count.ToString());
        foreach (var p in skippedClientOnly)
            sb.Append("  ").AppendLine(p);
        if (analysis.OverrideListSkipCount > 0)
            sb.Append("  Override list: ").AppendLine(analysis.OverrideListSkipCount.ToString());
        if (analysis.InJarMetadataSkipCount > 0)
            sb.Append("  In-jar metadata: ").AppendLine(analysis.InJarMetadataSkipCount.ToString());
        if (!string.IsNullOrEmpty(retainedArchivePath))
            sb.Append("Original archive retained: ").AppendLine(retainedArchivePath);
        if (warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in warnings)
                sb.Append("  ").AppendLine(w);
        }

        return sb.ToString().TrimEnd();
    }
}
