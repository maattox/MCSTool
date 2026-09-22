using System.Runtime.InteropServices;
using System.Windows;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfClipboard : IClipboard
{
    private readonly IUiDispatcher _dispatcher;

    public WpfClipboard(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Blazor click handlers run on the WPF dispatcher. SetText inline then
        // CLIPBRD_E_CANT_OPEN because WebView2 still holds the clipboard.
        await Task.Yield();

        ExternalException? last = null;
        for (var i = 0; i < 6; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i > 0)
                await Task.Delay(50 * i, cancellationToken).ConfigureAwait(false);

            try
            {
                await _dispatcher.InvokeAsync(() => SetTextCore(text), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (ExternalException ex)
            {
                last = ex;
            }
        }

        if (last is not null)
            throw last;
        throw new InvalidOperationException("Clipboard unavailable.");
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        try
        {
            return await _dispatcher.InvokeAsync(GetTextCore, cancellationToken).ConfigureAwait(false);
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static void SetTextCore(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        Clipboard.SetDataObject(data, copy: true);
    }

    private static string? GetTextCore() =>
        Clipboard.ContainsText() ? Clipboard.GetText() : null;
}
