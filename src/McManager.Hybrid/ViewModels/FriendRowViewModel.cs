using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;

namespace McManager.Hybrid.ViewModels;

public partial class FriendRowViewModel : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _ip = "";

    [ObservableProperty]
    private bool _isAdmin;

    public static FriendRowViewModel FromEntry(FriendEntry entry) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString() : entry.Id,
            Name = entry.Name,
            Ip = entry.Ip,
            IsAdmin = entry.IsAdmin,
        };

    public FriendEntry ToEntry()
    {
        var ip = FriendRules.TryNormalizeAllowlistSource(Ip, out var source, out _)
            ? source.Stored
            : Ip.Trim();
        return new FriendEntry
        {
            Id = Id,
            Name = Name.Trim(),
            Ip = ip,
            IsAdmin = IsAdmin,
        };
    }
}
