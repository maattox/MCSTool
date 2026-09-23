namespace McManager.Hybrid.Ui;

/// <summary>
/// Clipboard helpers for Blazor click handlers. WPF <c>Clipboard.SetText</c> can
/// throw <c>CLIPBRD_E_CANT_OPEN</c> while WebView2 holds the clipboard; that must
/// not take down the Blazor circuit.
/// </summary>
internal static class ClipboardUx
{
    public static async Task<bool> TrySetTextAsync(
        IClipboard clipboard,
        string text,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
