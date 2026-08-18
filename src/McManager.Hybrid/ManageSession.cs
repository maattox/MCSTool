namespace McManager.Hybrid;

/// <summary>
/// Reloads <c>config.local.json</c>, rebuilds OCI/door clients, and notifies
/// manage ViewModels that captured the previous session. First-run Setup Close
/// and Connect-existing must call this <em>before</em> entering Manage — the
/// singleton clients are often constructed while the file did not exist.
/// </summary>
public sealed class ManageSession
{
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;

    public ManageSession(LocalConfigHost configHost, ManageCloudServices cloud)
    {
        _configHost = configHost;
        _cloud = cloud;
    }

    /// <summary>
    /// Stop using the current OCI/door clients. Fired before
    /// <see cref="ManageCloudServices.Rebuild"/> disposes them.
    /// </summary>
    public event EventHandler? ClientsRebuilding;

    /// <summary>
    /// Host + cloud clients now match the file on disk (including “file gone”
    /// after destroy). Rebind captured config/clients.
    /// </summary>
    public event EventHandler? Reloaded;

    /// <summary>
    /// Re-read <c>config.local.json</c>, rebuild cloud clients, notify subscribers.
    /// Safe when the file is missing (destroy / first-run).
    /// </summary>
    public void ReloadFromDisk()
    {
        _configHost.Reload();
        ClientsRebuilding?.Invoke(this, EventArgs.Empty);
        _cloud.Rebuild();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }
}
