using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;

namespace McManager.Hybrid.ViewModels;

public partial class BlacklistRowViewModel : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _ip = "";

    public static BlacklistRowViewModel FromEntry(BlacklistEntry entry) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString() : entry.Id,
            Name = entry.Name,
            Ip = entry.Ip,
        };

    public BlacklistEntry ToEntry()
    {
        var ip = FriendRules.TryNormalizeAllowlistSource(Ip, out var source, out _)
            ? source.Stored
            : Ip.Trim();
        return new BlacklistEntry
        {
            Id = Id,
            Name = Name.Trim(),
            Ip = ip,
        };
    }
}
