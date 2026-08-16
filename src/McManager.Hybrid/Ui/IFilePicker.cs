namespace McManager.Hybrid.Ui;

/// <summary>
/// Native desktop file/folder pickers. Not HTML <c>&lt;input type=file&gt;</c>.
/// </summary>
public interface IFilePicker
{
    Task<string?> OpenFileAsync(FilePickRequest? request = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> OpenFilesAsync(FilePickRequest? request = null, CancellationToken cancellationToken = default);

    Task<string?> SaveFileAsync(FileSaveRequest? request = null, CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(string? title = null, CancellationToken cancellationToken = default);
}

public sealed class FilePickRequest
{
    public string? Title { get; init; }

    public string? InitialDirectory { get; init; }

    public string? FileName { get; init; }

    public IReadOnlyList<FileTypeFilter>? Filters { get; init; }
}

public sealed class FileSaveRequest
{
    public string? Title { get; init; }

    public string? InitialDirectory { get; init; }

    public string? FileName { get; init; }

    /// <summary>Extension without a leading dot, e.g. <c>zip</c>.</summary>
    public string? DefaultExtension { get; init; }

    public IReadOnlyList<FileTypeFilter>? Filters { get; init; }

    public bool OverwritePrompt { get; init; } = true;
}

/// <param name="Name">Filter label, e.g. <c>Zip files</c>.</param>
/// <param name="Extensions">Extensions including the dot (<c>.zip</c>), or <c>.*</c> for all files.</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Extensions)
{
    public FileTypeFilter(string name, params string[] extensions)
        : this(name, (IReadOnlyList<string>)extensions)
    {
    }
}
