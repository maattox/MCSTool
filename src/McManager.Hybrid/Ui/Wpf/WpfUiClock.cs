namespace McManager.Hybrid.Ui.Wpf;

/// <summary>
/// Clock that resumes <see cref="IUiClock.Delay"/> on the WPF STA dispatcher so ViewModel
/// continuations can touch UI state. Periodic ticks use BCL <see cref="PeriodicTimer"/>;
/// marshal with <see cref="IUiDispatcher"/> if they mutate UI.
/// </summary>
public sealed class WpfUiClock : IUiClock
{
    private readonly IUiDispatcher _dispatcher;

    public WpfUiClock(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public async Task Delay(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() => { }, cancellationToken).ConfigureAwait(false);
    }

    public PeriodicTimer CreatePeriodicTimer(TimeSpan period) => new(period);
}
