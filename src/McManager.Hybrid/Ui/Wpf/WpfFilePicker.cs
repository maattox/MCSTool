using Microsoft.Win32;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfFilePicker : IFilePicker
{
    private readonly IUiDispatcher _dispatcher;

    public WpfFilePicker(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task<string?> OpenFileAsync(FilePickRequest? request = null, CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new OpenFileDialog
                {
                    Title = request?.Title ?? "Open",
                    Filter = ToWin32Filter(request?.Filters),
                    Multiselect = false,
                    CheckFileExists = true,
                    CheckPathExists = true,
                };
                ApplyPath(dialog, request?.InitialDirectory, request?.FileName);
                return Show(dialog) == true ? dialog.FileName : null;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> OpenFilesAsync(
        FilePickRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new OpenFileDialog
                {
                    Title = request?.Title ?? "Open",
                    Filter = ToWin32Filter(request?.Filters),
                    Multiselect = true,
                    CheckFileExists = true,
                    CheckPathExists = true,
                };
                ApplyPath(dialog, request?.InitialDirectory, request?.FileName);
                if (Show(dialog) != true)
                {
                    return (IReadOnlyList<string>)Array.Empty<string>();
                }

                return (IReadOnlyList<string>)dialog.FileNames;
            },
            cancellationToken);
    }

    public Task<string?> SaveFileAsync(FileSaveRequest? request = null, CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new SaveFileDialog
                {
                    Title = request?.Title ?? "Save",
                    Filter = ToWin32Filter(request?.Filters),
                    OverwritePrompt = request?.OverwritePrompt ?? true,
                    AddExtension = true,
                    CheckPathExists = true,
                };
                if (!string.IsNullOrWhiteSpace(request?.DefaultExtension))
                {
                    dialog.DefaultExt = request.DefaultExtension.TrimStart('.');
                }

                ApplyPath(dialog, request?.InitialDirectory, request?.FileName);
                return Show(dialog) == true ? dialog.FileName : null;
            },
            cancellationToken);
    }

    public Task<string?> PickFolderAsync(string? title = null, CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dialog = new OpenFolderDialog
                {
                    Title = title ?? "Select folder",
                };
                var owner = System.Windows.Application.Current?.MainWindow;
                var ok = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                return ok == true ? dialog.FolderName : null;
            },
            cancellationToken);
    }

    private static bool? Show(FileDialog dialog)
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static void ApplyPath(FileDialog dialog, string? initialDirectory, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            dialog.FileName = fileName;
        }
    }

    internal static string ToWin32Filter(IReadOnlyList<FileTypeFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return "All files (*.*)|*.*";
        }

        return string.Join("|", filters.Select(ToWin32Filter));
    }

    private static string ToWin32Filter(FileTypeFilter filter)
    {
        var patterns = filter.Extensions.Count == 0
            ? "*.*"
            : string.Join(";", filter.Extensions.Select(ToWin32Pattern));
        return $"{filter.Name}|{patterns}";
    }

    private static string ToWin32Pattern(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || extension is "*" or ".*")
        {
            return "*.*";
        }

        return extension.StartsWith('.') ? "*" + extension : "*." + extension;
    }
}
