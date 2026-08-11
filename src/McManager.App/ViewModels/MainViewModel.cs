using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string Placeholder = "—";

    public string Title { get; } = "OCI MC Server Manager";

    [ObservableProperty]
    private string _status = "Loading local config…";

    [ObservableProperty]
    private string _playIp = Placeholder;

    [ObservableProperty]
    private string _playersDisplay = Placeholder;

    [ObservableProperty]
    private string _todayUsageDisplay = Placeholder;

    [ObservableProperty]
    private bool _configLoaded;

    public WhitelistViewModel? Whitelist { get; private set; }

    public MainViewModel()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        var loaded = LocalConfigStore.Load();
        if (!loaded.Succeeded || loaded.Config is null)
        {
            Status = loaded.Error ?? "Local config failed to load.";
            return;
        }

        ConfigLoaded = true;
        var cfg = loaded.Config;
        PlayIp = string.IsNullOrWhiteSpace(cfg.Play.ReservedPublicIp)
            ? Placeholder
            : cfg.Play.ReservedPublicIp;

        Whitelist = new WhitelistViewModel(
            cfg,
            loaded.Friends,
            loaded.DataDirectory ?? "");

        var warn = loaded.Warnings.Count == 0
            ? ""
            : $" ({loaded.Warnings.Count} config warning(s))";

        Status = $"Connecting to OCI…{warn}";

        var sessionResult = OciSession.TryCreate(cfg);
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            Status = $"OCI error: {sessionResult.Error}{warn}";
            return;
        }

        using var session = sessionResult.Value;
        var compute = new ComputeService(session);
        var lifecycle = await compute.GetLifecycleStateAsync(cfg.Vm1.InstanceId);

        Status = lifecycle.Succeeded
            ? $"VM1 lifecycle: {lifecycle.Value}{warn}"
            : $"OCI error: {lifecycle.Error}{warn}";
    }
}
