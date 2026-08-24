namespace McManager.Hybrid.Ui;

/// <summary>
/// Custom WPF caption from Blazor: drag (with Aero snap), min / max / close.
/// </summary>
public interface IWindowChromeService
{
    bool IsMaximized { get; }

    event EventHandler? Changed;

    void DragMove();

    void Minimize();

    void ToggleMaximize();

    void Close();
}
