using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.App.ViewModels;

public partial class WhitelistViewModel : ViewModelBase
{
    private readonly ManagerLocalConfig _config;
    private readonly string _dataDirectory;

    public ObservableCollection<FriendRowViewModel> Friends { get; } = [];

    [ObservableProperty]
    private FriendRowViewModel? _selectedFriend;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private string _editIp = "";

    [ObservableProperty]
    private bool _editIsAdmin;

    [ObservableProperty]
    private string _adminIpInput = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public WhitelistViewModel(
        ManagerLocalConfig config,
        FriendsLocalFile? friends,
        string dataDirectory)
    {
        _config = config;
        _dataDirectory = dataDirectory;

        foreach (var entry in friends?.Friends ?? [])
            Friends.Add(FriendRowViewModel.FromEntry(entry));
    }

    partial void OnSelectedFriendChanged(FriendRowViewModel? value)
    {
        if (value is null)
            return;

        EditName = value.Name;
        EditIp = value.Ip;
        EditIsAdmin = value.IsAdmin;
    }

    [RelayCommand]
    private void AddFriend()
    {
        if (!TryValidateEdit(out var error))
        {
            StatusMessage = error;
            return;
        }

        Friends.Add(new FriendRowViewModel
        {
            Name = EditName.Trim(),
            Ip = FriendRules.NormalizeIp(EditIp),
            IsAdmin = EditIsAdmin,
        });

        ClearEditFields();
        StatusMessage = "Friend added (not saved yet).";
    }

    [RelayCommand]
    private void UpdateSelected()
    {
        if (SelectedFriend is null)
        {
            StatusMessage = "Select a friend to update.";
            return;
        }

        if (!TryValidateEdit(out var error))
        {
            StatusMessage = error;
            return;
        }

        SelectedFriend.Name = EditName.Trim();
        SelectedFriend.Ip = FriendRules.NormalizeIp(EditIp);
        SelectedFriend.IsAdmin = EditIsAdmin;
        StatusMessage = "Friend updated (not saved yet).";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedFriend is null)
        {
            StatusMessage = "Select a friend to remove.";
            return;
        }

        Friends.Remove(SelectedFriend);
        SelectedFriend = null;
        ClearEditFields();
        StatusMessage = "Friend removed (not saved yet).";
    }

    [RelayCommand]
    private void SaveFriends()
    {
        var file = new FriendsLocalFile
        {
            SchemaVersion = 1,
            Friends = Friends.Select(f => f.ToEntry()).ToList(),
        };

        var result = LocalConfigStore.SaveFriends(file, _dataDirectory);
        StatusMessage = result.Succeeded
            ? $"Saved {file.Friends.Count} friend(s) to friends.local.json."
            : result.Error ?? "Save failed.";
    }

    [RelayCommand]
    private async Task SyncToOciAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Syncing Security List…";

        try
        {
            var sessionResult = OciSession.TryCreate(_config);
            if (!sessionResult.Succeeded || sessionResult.Value is null)
            {
                StatusMessage = sessionResult.Error ?? "OCI session failed.";
                return;
            }

            using var session = sessionResult.Value;
            var securityList = new SecurityListService(session);
            var friends = Friends.Select(f => f.ToEntry()).ToList();
            var result = await securityList.ApplyFriendsAsync(
                friends,
                _config.Network.SecurityListId,
                _config.Network.MinecraftPort,
                _config.Network.SshPort,
                _config.Door.HttpPort);

            StatusMessage = result.Succeeded
                ? result.Value?.Summary ?? "Sync complete."
                : result.Error ?? "Sync failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DetectPublicIpAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Detecting public IP…";

        try
        {
            var result = await PublicIpDetector.FetchPublicIpAsync();
            if (result.Succeeded && result.Value is not null)
            {
                AdminIpInput = result.Value;
                StatusMessage = $"Detected public IP: {result.Value}";
            }
            else
            {
                StatusMessage = result.Error ?? "Could not detect public IP.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAdminIpAsync()
    {
        if (IsBusy)
            return;

        if (!FriendRules.TryNormalizeIp(AdminIpInput, out var newIp))
        {
            StatusMessage = "Enter a valid IPv4 address for admin IP.";
            return;
        }

        var admin = FindAdminFriend();
        if (admin is null)
        {
            StatusMessage = "No admin friend found. Mark a friend as Admin or set admin_name in config.";
            return;
        }

        admin.Ip = newIp;
        admin.IsAdmin = true;
        if (string.IsNullOrWhiteSpace(admin.Name) && !string.IsNullOrWhiteSpace(_config.AdminName))
            admin.Name = _config.AdminName;

        EditName = admin.Name;
        EditIp = admin.Ip;
        EditIsAdmin = true;
        SelectedFriend = admin;

        SaveFriends();
        await SyncToOciAsync();
    }

    private FriendRowViewModel? FindAdminFriend()
    {
        if (!string.IsNullOrWhiteSpace(_config.AdminName))
        {
            var byName = Friends.FirstOrDefault(
                f => string.Equals(f.Name, _config.AdminName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        return Friends.FirstOrDefault(f => f.IsAdmin);
    }

    private bool TryValidateEdit(out string error)
    {
        if (string.IsNullOrWhiteSpace(EditIp))
        {
            error = "IP is required.";
            return false;
        }

        if (!FriendRules.TryNormalizeIp(EditIp, out _))
        {
            error = "Enter a valid IPv4 address.";
            return false;
        }

        error = "";
        return true;
    }

    private void ClearEditFields()
    {
        EditName = "";
        EditIp = "";
        EditIsAdmin = false;
    }
}
