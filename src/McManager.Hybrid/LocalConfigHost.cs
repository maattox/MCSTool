using McManager.Core.Config;

namespace McManager.Hybrid;

/// <summary>
/// Local manage-config load at host startup (and after Connect-existing). File I/O
/// only — no OCI probe. Power / poll live on <see cref="ViewModels.MainViewModel"/>.
/// </summary>
public sealed class LocalConfigHost
{
    public const string Placeholder = "—";

    public LocalConfigLoadResult LoadResult { get; private set; } = null!;

    public ManagerLocalConfig? Config { get; private set; }

    /// <summary>
    /// Same meaning as <see cref="LocalConfigStore.HasManageConfig"/>, from the
    /// already-loaded result (do not probe OCI to decide this).
    /// </summary>
    public bool HasManageConfig { get; private set; }

    public bool ShowFirstRun => !HasManageConfig;

    /// <summary>True when <c>config.local.json</c> exists in the resolved data dir.</summary>
    public bool ConfigFileExists { get; private set; }

    /// <summary>File was present but did not parse — show the error on the first-run chooser.</summary>
    public bool LoadFailed { get; private set; }

    public string PlayIp { get; private set; } = Placeholder;

    public bool HasPlayIp { get; private set; }

    public LocalConfigHost()
    {
        Reload();
    }

    /// <summary>
    /// Re-read <c>config.local.json</c> after Connect-existing writes it. Still no OCI.
    /// Call before constructing manage ViewModels so they see the new seed.
    /// </summary>
    public void Reload()
    {
        LoadResult = LocalConfigStore.Load();
        ConfigFileExists = LocalConfigStore.ConfigFileExists();
        HasManageConfig = LoadResult.Succeeded && LoadResult.Config is not null;
        LoadFailed = !HasManageConfig && ConfigFileExists;
        Config = LoadResult.Config;

        var ip = Config?.Play.ReservedPublicIp;
        HasPlayIp = !string.IsNullOrWhiteSpace(ip);
        PlayIp = HasPlayIp ? ip! : Placeholder;
    }
}
