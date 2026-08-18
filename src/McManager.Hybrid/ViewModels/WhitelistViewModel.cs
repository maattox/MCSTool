using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Whitelist tab: local friends CRUD + Security List apply (private allowlist + CIDR).
/// Dialogs for add/update stay in Razor. Does not touch manage-chrome power-in-flight.
/// </summary>
public sealed partial class WhitelistViewModel : ObservableObject
{
    private ManagerLocalConfig? _config;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private string _dataDirectory = "";
    private ISecurityListService? _securityList;
    private AllowlistStore? _allowlistStore;
    private string? _sessionError;
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

    public WhitelistViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _dataDirectory = "";
        Friends.CollectionChanged += OnFriendsCollectionChanged;
        BindFromHost();
        _session.Reloaded += OnSessionReloaded;
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _dataDirectory = _configHost.LoadResult.DataDirectory ?? "";
        _sessionError = _cloud.SessionError;
        _securityList = _cloud.Session is not null
            ? new SecurityListService(_cloud.Session)
            : null;
        var osReady = _cloud.Session is not null
            && _config is not null
            && !string.IsNullOrWhiteSpace(_config.ObjectStorage.Namespace)
            && !string.IsNullOrWhiteSpace(_config.ObjectStorage.Bucket);
        _allowlistStore = osReady
            ? new AllowlistStore(
                new ObjectStorageService(_cloud.Session!, _config!.ObjectStorage),
                _config.ObjectStorage.Prefixes)
            : null;

        Friends.Clear();
        foreach (var entry in _configHost.LoadResult.Friends?.Friends ?? [])
            Friends.Add(FriendRowViewModel.FromEntry(entry));

        CaptureSavedFingerprint();
        if (_config is null)
            StatusMessage = string.IsNullOrWhiteSpace(_sessionError)
                ? ""
                : _sessionError;
    }

    public bool TryAddFriend(string name, string ip, bool isAdmin, bool requireSingleHost = true)
    {
        if (!TryValidate(name, ip, isAdmin, editing: null, requireSingleHost, out var error, out var normalized))
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

    public bool TryUpdateFriend(
        FriendRowViewModel? row,
        string name,
        string ip,
        bool isAdmin,
        bool requireSingleHost = true)
    {
        if (row is null)
            return false;

        if (!TryValidate(name, ip, isAdmin, row, requireSingleHost, out var error, out var normalized))
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

        if (!SaveFriendsLocal())
            return;

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

    private bool SaveFriendsLocal()
    {
        var file = new FriendsLocalFile
        {
            SchemaVersion = 1,
            Friends = Friends.Select(f => f.ToEntry()).ToList(),
        };

        var result = LocalConfigStore.SaveFriends(file, _dataDirectory);
        if (!result.Succeeded)
        {
            StatusMessage = result.Error ?? "Save failed.";
            return false;
        }

        return true;
    }

    private async Task SyncToOciAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var applied = await ApplySecurityListUnlockedAsync();
            if (applied is null)
                return;

            var summary = applied.Summary;
            var friends = Friends.Select(f => f.ToEntry()).ToList();
            if (_allowlistStore is not null)
            {
                var os = await _allowlistStore.PublishIfPresentAsync(friends);
                if (!os.Succeeded)
                {
                    StatusMessage = summary
                        + "\nObject Storage allowlist update failed: "
                        + (os.Error ?? "unknown");
                    return;
                }

                if (os.Value is { SkippedMissing: false })
                    summary += "\n" + os.Value.Message;
            }

            StatusMessage = summary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<SecurityListApplyResult?> ApplySecurityListUnlockedAsync()
    {
        StatusMessage = "Applying allowlist…";

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return null;
        }

        if (_securityList is null)
        {
            StatusMessage = _sessionError ?? "Cloud session failed.";
            return null;
        }

        var friends = Friends.Select(f => f.ToEntry()).ToList();
        var result = await _securityList.ApplyFriendsAsync(
            friends,
            _config.Network.SecurityListId,
            _config.Network.MinecraftPort,
            _config.Network.SshPort,
            _config.Door.HttpPort,
            _config.AdminName);

        if (!result.Succeeded)
        {
            StatusMessage = result.Error ?? "Security List update failed.";
            return null;
        }

        return result.Value;
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

    public string? PrefixWidthWarning(string source)
    {
        if (!FriendRules.TryNormalizeAllowlistSource(source, out var parsed, out _))
            return null;
        return FriendRules.WidthWarning(parsed);
    }

    private bool TryValidate(
        string name,
        string ip,
        bool isAdmin,
        FriendRowViewModel? editing,
        bool requireSingleHost,
        out string error,
        out string normalized)
    {
        normalized = "";
        if (!FriendRules.TryNormalizeAllowlistSource(ip, out var source, out error))
            return false;

        if (requireSingleHost && !source.IsSingleHost)
        {
            error = "Use Advanced to enter a CIDR prefix.";
            return false;
        }

        if (isAdmin && !source.IsSingleHost && !AllowsOwnAdminPrefix(name, editing))
        {
            error = "Admin SSH and doorbell stay a single IPv4 unless you are editing your own admin entry. Uncheck Admin or use a /32.";
            return false;
        }

        normalized = source.Stored;
        error = "";
        return true;
    }

    private bool AllowsOwnAdminPrefix(string name, FriendRowViewModel? editing)
    {
        var admin = FindAdminFriend();
        if (editing is not null && admin is not null && editing.Id == admin.Id)
            return true;

        return !string.IsNullOrWhiteSpace(_config?.AdminName)
            && string.Equals(name.Trim(), _config.AdminName, StringComparison.OrdinalIgnoreCase);
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
