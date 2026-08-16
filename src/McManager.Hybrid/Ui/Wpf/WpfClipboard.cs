using System.Runtime.InteropServices;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfClipboard : IClipboard
{
    private readonly IUiDispatcher _dispatcher;

    public WpfClipboard(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _dispatcher.InvokeAsync(() => SetTextWithRetry(text), cancellationToken);
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(
            () => System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : null,
            cancellationToken);
    }

    private static void SetTextWithRetry(string text)
    {
        const int tries = 5;
        for (var i = 0; i < tries; i++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                return;
            }
            catch (COMException) when (i < tries - 1)
            {
                Thread.Sleep(50);
            }
        }
    }
}
