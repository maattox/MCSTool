using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Notifications;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Window-locked action banner. Posts go through <see cref="ActionBanner"/>.
/// Short success auto-hides; long copy, progress, warning, and error wait for X.
/// </summary>
public sealed partial class ActionBannerViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ShortSuccessHide = TimeSpan.FromSeconds(3.5);

    private readonly ActionBanner _banner;
    private readonly IUiClock _clock;
    private CancellationTokenSource? _hideCts;

    public ActionBannerViewModel(ActionBanner banner, IUiClock clock)
    {
        _banner = banner;
        _clock = clock;
        _banner.Changed += OnBannerChanged;
        CopyFromBanner();
    }

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private ActionBannerSeverity _severity;

    public string SeverityClass =>
        Severity switch
        {
            ActionBannerSeverity.Error => "is-error",
            ActionBannerSeverity.Warning => "is-warning",
            ActionBannerSeverity.Progress => "is-progress",
            _ => "is-success",
        };

    public string SeverityIcon =>
        Severity switch
        {
            ActionBannerSeverity.Error => "ti ti-alert-triangle",
            ActionBannerSeverity.Warning => "ti ti-alert-triangle",
            ActionBannerSeverity.Progress => "ti ti-loader",
            _ => "ti ti-circle-check",
        };

    public string LiveRole =>
        Severity is ActionBannerSeverity.Error or ActionBannerSeverity.Warning ? "alert" : "status";

    public string AriaLive =>
        Severity is ActionBannerSeverity.Error or ActionBannerSeverity.Warning ? "assertive" : "polite";

    public void Dismiss() => _banner.Dismiss();

    public void Dispose()
    {
        _banner.Changed -= OnBannerChanged;
        _hideCts?.Cancel();
        _hideCts?.Dispose();
    }

    private void OnBannerChanged(object? sender, EventArgs e)
    {
        CopyFromBanner();
        OnPropertyChanged(nameof(SeverityClass));
        OnPropertyChanged(nameof(SeverityIcon));
        OnPropertyChanged(nameof(LiveRole));
        OnPropertyChanged(nameof(AriaLive));

        _hideCts?.Cancel();
        _hideCts?.Dispose();
        _hideCts = null;
        if (!IsVisible || ActionBanner.ShouldPersist(Message, Severity))
            return;

        var cts = new CancellationTokenSource();
        _hideCts = cts;
        _ = HideAfterAsync(cts.Token);
    }

    private void CopyFromBanner()
    {
        Message = _banner.Message;
        Severity = _banner.Severity;
        IsVisible = _banner.IsVisible;
    }

    private async Task HideAfterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _clock.Delay(ShortSuccessHide, cancellationToken).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested)
                _banner.Dismiss();
        }
        catch (OperationCanceledException)
        {
        }
    }
}
