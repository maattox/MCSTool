using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Whitelist tab: local friends CRUD + Security List apply. Dialogs stay in Razor.
/// Does not touch manage-chrome power-in-flight.
/// </summary>
public sealed partial class WhitelistViewModel : ObservableObject
{
    private readonly ManagerLocalConfig? _config;
    private readonly string _dataDirectory;
    private readonly ISecurityListService? _securityList;
    private readonly string? _sessionError;
    private string _savedFingerprint = "";

    public ObservableCollection<FriendRowViewModel> Friends { get; } = [];

    [ObservableProperty]
    private string _adminIpInput = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasPendingChanges;

    public bool CanSaveChanges => HasPendingChanges && !IsBusy;

    public WhitelistViewModel(LocalConfigHost configHost, ManageCloudServices cloud)
    {
        _config = configHost.Config;
        _dataDirectory = configHost.LoadResult.DataDirectory ?? "";
        _sessionError = cloud.SessionError;
        if (cloud.Session is not null)
            _securityList = new SecurityListService(cloud.Session);

        Friends.CollectionChanged += OnFriendsCollectionChanged;
        foreach (var entry in configHost.LoadResult.Friends?.Friends ?? [])
            Friends.Add(FriendRowViewModel.FromEntry(entry));
        CaptureSavedFingerprint();
    }

    public bool TryAddFriend(string name, string ip, bool isAdmin)
    {
        if (!TryValidate(name, ip, out var error, out var normalized))
        {
            StatusMessage = error;
            return false;
        }

        Friends.Add(new FriendRowViewModel
        {
            Name = name.Trim(),
            Ip = normalized,
            IsAdmin = isAdmin,
        });
        StatusMessage = "Friend added (not saved yet).";
        RecalculateDirty();
        return true;
    }

    public bool TryUpdateFriend(FriendRowViewModel? row, string name, string ip, bool isAdmin)
    {
        if (row is null)
            return false;

        if (!TryValidate(name, ip, out var error, out var normalized))
        {
            StatusMessage = error;
            return false;
        }

        row.Name = name.Trim();
        row.Ip = normalized;
        row.IsAdmin = isAdmin;
        StatusMessage = "Friend updated (not saved yet).";
        RecalculateDirty();
        NotifyFriendsUi();
        return true;
    }

    public void RemoveFriend(FriendRowViewModel? row)
    {
        if (row is null)
            return;

        Friends.Remove(row);
        StatusMessage = "Friend removed (not saved yet).";
    }

    public async Task SaveChangesAsync()
    {
        if (IsBusy || !HasPendingChanges)
            return;

        SaveFriendsLocal();
        await SyncToOciAsync();
        if (StatusMessage.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return;

        CaptureSavedFingerprint();
    }

    public async Task DetectPublicIpAsync()
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

    public async Task UpdateAdminIpAsync()
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
        if (string.IsNullOrWhiteSpace(admin.Name) && !string.IsNullOrWhiteSpace(_config?.AdminName))
            admin.Name = _config.AdminName;

        RecalculateDirty();
        NotifyFriendsUi();
        if (!HasPendingChanges)
        {
            StatusMessage = "Admin IP is already that address.";
            return;
        }

        await SaveChangesAsync();
    }

    private void SaveFriendsLocal()
    {
        var file = new FriendsLocalFile
        {
            SchemaVersion = 1,
            Friends = Friends.Select(f => f.ToEntry()).ToList(),
        };

        var result = LocalConfigStore.SaveFriends(file, _dataDirectory);
        if (!result.Succeeded)
            StatusMessage = result.Error ?? "Save failed.";
    }

    private async Task SyncToOciAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Applying allowlist…";

        try
        {
            if (_config is null)
            {
                StatusMessage = "Local config is missing.";
                return;
            }

            if (_securityList is null)
            {
                StatusMessage = _sessionError ?? "Cloud session failed.";
                return;
            }

            var friends = Friends.Select(f => f.ToEntry()).ToList();
            var result = await _securityList.ApplyFriendsAsync(
                friends,
                _config.Network.SecurityListId,
                _config.Network.MinecraftPort,
                _config.Network.SshPort,
                _config.Door.HttpPort);

            StatusMessage = result.Succeeded
                ? result.Value?.Summary ?? "Saved and applied."
                : result.Error ?? "Sync failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private FriendRowViewModel? FindAdminFriend()
    {
        if (!string.IsNullOrWhiteSpace(_config?.AdminName))
        {
            var byName = Friends.FirstOrDefault(
                f => string.Equals(f.Name, _config.AdminName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        return Friends.FirstOrDefault(f => f.IsAdmin);
    }

    private static bool TryValidate(string name, string ip, out string error, out string normalized)
    {
        _ = name;
        normalized = "";
        if (string.IsNullOrWhiteSpace(ip))
        {
            error = "IP is required.";
            return false;
        }

        if (!FriendRules.TryNormalizeIp(ip, out normalized))
        {
            error = "Enter a valid IPv4 address.";
            return false;
        }

        error = "";
        return true;
    }

    private void OnFriendsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FriendRowViewModel row in e.OldItems)
                row.PropertyChanged -= OnFriendPropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (FriendRowViewModel row in e.NewItems)
                row.PropertyChanged += OnFriendPropertyChanged;
        }

        RecalculateDirty();
        NotifyFriendsUi();
    }

    private void OnFriendPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        RecalculateDirty();

    private void RecalculateDirty()
    {
        HasPendingChanges = Fingerprint() != _savedFingerprint;
    }

    private void CaptureSavedFingerprint()
    {
        _savedFingerprint = Fingerprint();
        HasPendingChanges = false;
    }

    private string Fingerprint() =>
        string.Join("\n", Friends.Select(f => $"{f.Name}\t{f.Ip}\t{f.IsAdmin}"));

    private void NotifyFriendsUi() =>
        OnPropertyChanged(nameof(Friends));

    partial void OnHasPendingChangesChanged(bool value) =>
        OnPropertyChanged(nameof(CanSaveChanges));

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanSaveChanges));
}
