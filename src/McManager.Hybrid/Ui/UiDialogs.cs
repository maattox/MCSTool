namespace McManager.Hybrid.Ui;

/// <summary>
/// In-process dialog queue consumed by <c>ModalHost</c>. ViewModels depend on <see cref="IUiDialogs"/> only.
/// </summary>
public sealed class UiDialogs : IUiDialogs
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? CurrentChanged;

    public DialogSession? Current { get; private set; }

    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmButtonText = "OK",
        CancellationToken cancellationToken = default)
    {
        var tcs = NewTcs<bool>();
        return RunAsync(
            new ConfirmSession(title, message, confirmButtonText, tcs),
            tcs.Task,
            cancellationToken);
    }

    public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var tcs = NewTcs<bool>();
        return RunAsync(new InfoSession(title, message, tcs), tcs.Task, cancellationToken);
    }

    public Task<string?> ChooseAsync(
        string title,
        string message,
        IReadOnlyList<UiChoice> choices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var tcs = NewTcs<string?>();
        return RunAsync(new ChooseSession(title, message, choices, tcs), tcs.Task, cancellationToken);
    }

    public void CompleteConfirm(bool confirmed)
    {
        if (Current is ConfirmSession session)
        {
            session.Completion.TrySetResult(confirmed);
        }
    }

    public void CompleteInfo()
    {
        if (Current is InfoSession session)
        {
            session.Completion.TrySetResult(true);
        }
    }

    public void CompleteChoose(string? id)
    {
        if (Current is ChooseSession session)
        {
            session.Completion.TrySetResult(id);
        }
    }

    public void Dismiss()
    {
        switch (Current)
        {
            case ConfirmSession confirm:
                confirm.Completion.TrySetResult(false);
                break;
            case InfoSession info:
                info.Completion.TrySetResult(true);
                break;
            case ChooseSession choose:
                choose.Completion.TrySetResult(null);
                break;
        }
    }

    private async Task<T> RunAsync<T>(
        DialogSession session,
        Task<T> completion,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Current = session;
            CurrentChanged?.Invoke();
            return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Current = null;
            CurrentChanged?.Invoke();
            _gate.Release();
        }
    }

    private static TaskCompletionSource<T> NewTcs<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public abstract class DialogSession
{
}

public sealed class ConfirmSession : DialogSession
{
    internal ConfirmSession(
        string title,
        string message,
        string confirmButtonText,
        TaskCompletionSource<bool> completion)
    {
        Title = title;
        Message = message;
        ConfirmButtonText = confirmButtonText;
        Completion = completion;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmButtonText { get; }

    internal TaskCompletionSource<bool> Completion { get; }
}

public sealed class InfoSession : DialogSession
{
    internal InfoSession(string title, string message, TaskCompletionSource<bool> completion)
    {
        Title = title;
        Message = message;
        Completion = completion;
    }

    public string Title { get; }

    public string Message { get; }

    internal TaskCompletionSource<bool> Completion { get; }
}

public sealed class ChooseSession : DialogSession
{
    internal ChooseSession(
        string title,
        string message,
        IReadOnlyList<UiChoice> choices,
        TaskCompletionSource<string?> completion)
    {
        Title = title;
        Message = message;
        Choices = choices;
        Completion = completion;
    }

    public string Title { get; }

    public string Message { get; }

    public IReadOnlyList<UiChoice> Choices { get; }

    internal TaskCompletionSource<string?> Completion { get; }
}
