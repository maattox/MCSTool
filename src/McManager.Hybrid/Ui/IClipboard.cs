namespace McManager.Hybrid.Ui;

public interface IClipboard
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
}
