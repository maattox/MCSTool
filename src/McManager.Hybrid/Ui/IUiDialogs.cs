namespace McManager.Hybrid.Ui;

/// <summary>
/// Confirm / info / chooser dialogs. Hosted as Razor modals (mockup overlay CSS), not WPF MessageBox.
/// </summary>
public interface IUiDialogs
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmButtonText = "OK",
        CancellationToken cancellationToken = default);

    Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <returns>The selected <see cref="UiChoice.Id"/>, or <c>null</c> if cancelled.</returns>
    Task<string?> ChooseAsync(
        string title,
        string message,
        IReadOnlyList<UiChoice> choices,
        CancellationToken cancellationToken = default);
}

public sealed record UiChoice(string Id, string Label, string? Description = null);
