using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Services;

namespace McManager.Hybrid.ViewModels;

public partial class PlayerRowViewModel : ObservableObject
{
    public PlayerRowViewModel(OnlinePlayer player)
    {
        Name = player.Name;
        Uuid = player.Uuid;
        UuidHyphenless = player.UuidHyphenless;
    }

    public string Name { get; }

    public string Uuid { get; }

    public string UuidHyphenless { get; }

    public bool HasUuid => UuidHyphenless.Length == 32;

    [ObservableProperty]
    private string _faceDataUrl = "";

    public bool SameAs(OnlinePlayer player) =>
        string.Equals(Name, player.Name, StringComparison.Ordinal)
        && string.Equals(UuidHyphenless, player.UuidHyphenless, StringComparison.Ordinal);
}
