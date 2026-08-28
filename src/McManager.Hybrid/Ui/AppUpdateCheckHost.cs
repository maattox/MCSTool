using McManager.Core.Services;
using McManager.Hybrid.ViewModels;

namespace McManager.Hybrid.Ui;

/// <summary>
/// One GitHub Releases check after the Blazor UI is up. Prompt only — never
/// applies an installer. Failures are quiet (no crash, no retry loop).
/// </summary>
public sealed class AppUpdateCheckHost
{
    private readonly ChromeViewModel _chrome;
    private readonly GitHubLatestReleaseClient _client;
    private readonly IUiDialogs _dialogs;
    private readonly IShell _shell;
    private readonly IUiDispatcher _dispatcher;
    private int _started;

    public AppUpdateCheckHost(
        ChromeViewModel chrome,
        GitHubLatestReleaseClient client,
        IUiDialogs dialogs,
        IShell shell,
        IUiDispatcher dispatcher)
    {
        _chrome = chrome;
        _client = client;
        _dialogs = dialogs;
        _shell = shell;
        _dispatcher = dispatcher;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            var prompt = await AppUpdateCheck.EvaluateAsync(
                _chrome.CheckForUpdates,
                _chrome.AppVersion,
                _client,
                cancellationToken).ConfigureAwait(false);
            if (prompt is null)
                return;
            if (!_chrome.CheckForUpdates)
                return;

            var open = await _dispatcher.InvokeAsync(
                () => _dialogs.ConfirmAsync(
                    prompt.Title,
                    prompt.Message,
                    AppUpdateCheck.OpenDownloadButton,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!open)
                return;

            await _dispatcher.InvokeAsync(() => _shell.OpenUrl(prompt.OpenUrl), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch
        {
            // Offline / unexpected: Manager still opens.
        }
    }
}
