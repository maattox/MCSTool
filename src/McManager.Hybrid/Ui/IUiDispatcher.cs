namespace McManager.Hybrid.Ui;

/// <summary>
/// Marshals work onto the UI thread (STA in this host). ViewModels should not
/// reference host windowing types.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
}
