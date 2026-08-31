using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace McManager.Hybrid;

/// <summary>
/// Evergreen WebView2 is required for <c>BlazorWebView</c>. Do not bundle a runtime
/// (the Windows installer does not ship WebView2).
/// </summary>
internal static class WebView2RuntimeGuard
{
    internal const string EvergreenInstallerUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>
    /// Probe before showing the Blazor host. Missing Evergreen typically throws
    /// <see cref="WebView2RuntimeNotFoundException"/> or a <see cref="COMException"/>.
    /// </summary>
    internal static bool TryEnsureRuntime(out string? error)
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(version))
            {
                error = "WebView2 Runtime was not found.";
                return false;
            }

            error = null;
            return true;
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            // WebView2 Evergreen runtime missing — WPF MessageBox + installer link. Do not bundle.
            error = ex.Message;
            return false;
        }
        catch (COMException ex)
        {
            // COM failure while probing WebView2 (often the same missing-runtime case).
            error = ex.Message;
            return false;
        }
    }

    internal static bool IsMissingRuntime(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is WebView2RuntimeNotFoundException)
            {
                return true;
            }

            if (current is COMException &&
                current.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ShowMissingRuntimeMessage(string? detail)
    {
        var body =
            "Microsoft Edge WebView2 Runtime is required to run MCSTool." +
            Environment.NewLine + Environment.NewLine +
            "Install the Evergreen runtime from:" + Environment.NewLine +
            EvergreenInstallerUrl;

        if (!string.IsNullOrWhiteSpace(detail))
        {
            body += Environment.NewLine + Environment.NewLine + detail;
        }

        MessageBox.Show(
            body,
            "WebView2 Runtime missing",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
