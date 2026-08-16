namespace McManager.Hybrid.Ui;

/// <summary>
/// WPF window activation, forwarded to ViewModels without host window types.
/// Unfocused / minimized → background poll interval.
/// </summary>
public sealed class WindowFocusBroker
{
    public bool IsFocused { get; private set; } = true;

    public event Action<bool>? FocusChanged;

    public void SetFocused(bool focused)
    {
        if (IsFocused == focused)
            return;

        IsFocused = focused;
        FocusChanged?.Invoke(focused);
    }
}
