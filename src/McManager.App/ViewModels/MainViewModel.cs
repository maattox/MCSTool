using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;

namespace McManager.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Title { get; } = "OCI MC Server Manager";

    [ObservableProperty]
    private string _status = "Loading local config…";

    public MainViewModel()
    {
        var loaded = LocalConfigStore.Load();
        if (!loaded.Succeeded || loaded.Config is null)
        {
            Status = loaded.Error ?? "Local config failed to load.";
            return;
        }

        var cfg = loaded.Config;
        var friendCount = loaded.Friends?.Friends.Count ?? 0;
        var warn = loaded.Warnings.Count == 0
            ? "no validation warnings"
            : $"{loaded.Warnings.Count} validation warning(s)";

        Status =
            $"Config OK — region {cfg.Oci.Region}, play IP {cfg.Play.ReservedPublicIp}, "
            + $"{friendCount} friend(s), {warn}. Ready for manage MVP.";
    }
}
