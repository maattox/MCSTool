namespace McManager.Core.Setup;

/// <summary>
/// Outcome of a server-side-only <c>.mrpack</c> install into a destination directory
/// (blueprint §22.1 / §25.3). The original archive is retained for Phase 5 re-download.
/// </summary>
public sealed class MrpackInstallResult
{
    public MrpackInstallResult(
        MrpackAnalysis analysis,
        string destDirectory,
        string? retainedArchivePath,
        IReadOnlyList<string> installedRelativePaths,
        IReadOnlyList<string> skippedClientOnlyPaths,
        IReadOnlyList<string> copiedOverridePaths,
        IReadOnlyList<string> warnings,
        string summary)
    {
        Analysis = analysis;
        DestDirectory = destDirectory;
        RetainedArchivePath = retainedArchivePath;
        InstalledRelativePaths = installedRelativePaths;
        SkippedClientOnlyPaths = skippedClientOnlyPaths;
        CopiedOverridePaths = copiedOverridePaths;
        Warnings = warnings;
        Summary = summary;
    }

    public MrpackAnalysis Analysis { get; }
    public string DestDirectory { get; }
    public string? RetainedArchivePath { get; }
    public IReadOnlyList<string> InstalledRelativePaths { get; }
    public IReadOnlyList<string> SkippedClientOnlyPaths { get; }
    public IReadOnlyList<string> CopiedOverridePaths { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string Summary { get; }

    public const string ClientPackReminder =
        "Friends must install the same exported pack you imported. Keep that file "
        + "(Manager saved a copy for later re-download). This product cannot rebuild a "
        + "client pack from the server mods/ folder.";
}
