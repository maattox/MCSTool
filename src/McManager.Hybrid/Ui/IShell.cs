namespace McManager.Hybrid.Ui;

/// <summary>Open a folder/file in Explorer or an https URL in the default browser.</summary>
public interface IShell
{
    void OpenPath(string path);

    void OpenUrl(string url);
}
