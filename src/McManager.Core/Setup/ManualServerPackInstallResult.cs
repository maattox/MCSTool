namespace McManager.Core.Setup;

/// <summary>
/// Outcome of unzipping a generic server pack into a destination directory
/// (blueprint §24). The original archive is retained for Phase 5 re-download.
/// </summary>
public sealed class ManualServerPackInstallResult
{
    public ManualServerPackInstallResult(
        ManualServerPackAnalysis analysis,
        string destDirectory,
        string? retainedArchivePath,
        IReadOnlyList<string> installedRelativePaths,
        IReadOnlyList<string> skippedClientOnlyPaths,
        IReadOnlyList<string> warnings,
        string summary)
    {
        Analysis = analysis;
        DestDirectory = destDirectory;
        RetainedArchivePath = retainedArchivePath;
        InstalledRelativePaths = installedRelativePaths;
        SkippedClientOnlyPaths = skippedClientOnlyPaths;
        Warnings = warnings;
        Summary = summary;
    }

    public ManualServerPackAnalysis Analysis { get; }
    public string DestDirectory { get; }
    public string? RetainedArchivePath { get; }
    public IReadOnlyList<string> InstalledRelativePaths { get; }
    public IReadOnlyList<string> SkippedClientOnlyPaths { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string Summary { get; }
}
