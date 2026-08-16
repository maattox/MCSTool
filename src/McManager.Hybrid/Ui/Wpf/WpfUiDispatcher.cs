using System.Windows.Threading;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }

    public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (CheckAccess())
        {
            return action();
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (CheckAccess())
        {
            return Task.FromResult(action());
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task;
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (CheckAccess())
        {
            return action();
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).Task.Unwrap();
    }
}
