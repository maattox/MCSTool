using System.Diagnostics;
using System.IO;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfShell : IShell
{
    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Directory.Exists(path))
        {
            Start(path);
            return;
        }

        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select," + Quote(path),
                UseShellExecute = true,
            });
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            Start(parent);
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
    }

    private static void Start(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });

    private static string Quote(string path) =>
        "\"" + path.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
