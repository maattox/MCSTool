namespace McManager.Hybrid.Ui;

/// <summary>
/// Time source for ViewModels. Prefer <see cref="CreatePeriodicTimer"/> plus
/// <see cref="IUiDispatcher"/> instead of host-specific dispatcher timers.
/// </summary>
public interface IUiClock
{
    DateTimeOffset UtcNow { get; }

    Task Delay(TimeSpan delay, CancellationToken cancellationToken = default);

    PeriodicTimer CreatePeriodicTimer(TimeSpan period);
}
