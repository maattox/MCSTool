using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Players tab: online roster from the pin's <c>list uuids</c> poll; banned from <c>banlist</c>.
/// Kick/Mod/Unmod/Ban/Unban = RCON only.
/// </summary>
public sealed partial class PlayersViewModel : ObservableObject, IDisposable
{
    public const string HelpTitle =
        "Players currently connected. Hover a row to Kick, Mod, Unmod, or Ban. "
        + "Banned lists in-game bans; hover Unban. "
        + "Ban is Minecraft’s in-game ban only — it does not change Who can join. "
        + "Kick, Ban, and Mod may appear in operator chat (broadcast-rcon-to-ops).";

    public const string NobodyOnlineHint = "No one is online.";

    public const string NobodyBannedHint = "No banned players.";

    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private readonly ActionBanner _banner;
    private readonly CrafatarAvatarCache _avatars;
    private readonly IUiDispatcher _dispatcher;
    private ManagerLocalConfig? _config;
    private ISshService _ssh = null!;
    private bool _disposed;
    private bool _forwardBanner;
    private int _faceGen;

    public ObservableCollection<PlayerRowViewModel> Players { get; } = [];

    public ObservableCollection<PlayerRowViewModel> BannedPlayers { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    [NotifyPropertyChangedFor(nameof(EmptyHint))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    public bool Vm1IsRunning =>
        string.Equals(_main.Vm1Lifecycle, "RUNNING", StringComparison.OrdinalIgnoreCase);

    public bool MinecraftJoinable => DoorStatus.IsPlayableName(_main.DoorState);

    public bool CanAct => MinecraftJoinable && !IsBusy;

    public string EmptyHint =>
        MinecraftJoinable ? NobodyOnlineHint : MinecraftConsoleRemote.PlayersEmptyHint;

    public PlayersViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        MainViewModel main,
        ActionBanner banner,
        CrafatarAvatarCache avatars,
        IUiDispatcher dispatcher)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _main = main;
        _banner = banner;
        _avatars = avatars;
        _dispatcher = dispatcher;

        BindFromHost();
        _forwardBanner = true;
        _session.Reloaded += OnSessionReloaded;
        _main.PropertyChanged += OnMainChanged;
        SyncRoster();
        SyncBanned();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _faceGen++;
        _session.Reloaded -= OnSessionReloaded;
        _main.PropertyChanged -= OnMainChanged;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!_forwardBanner)
            return;
        _banner.ShowInferred(value);
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.OnlinePlayers) or null)
            SyncRoster();

        if (e.PropertyName is nameof(MainViewModel.BannedPlayers) or null)
            SyncBanned();

        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle)
            or nameof(MainViewModel.DoorState)
            or nameof(MainViewModel.StatusIsRunning)
            or null)
        {
            OnPropertyChanged(nameof(Vm1IsRunning));
            OnPropertyChanged(nameof(MinecraftJoinable));
            OnPropertyChanged(nameof(CanAct));
            OnPropertyChanged(nameof(EmptyHint));
        }
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
        NotifyDerived();
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Vm1IsRunning));
        OnPropertyChanged(nameof(MinecraftJoinable));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void SyncRoster()
    {
        var snapshot = _main.OnlinePlayers;
        var incoming = snapshot.ToDictionary(p => p.Name, StringComparer.Ordinal);

        for (var i = Players.Count - 1; i >= 0; i--)
        {
            var row = Players[i];
            if (!incoming.TryGetValue(row.Name, out var player) || !row.SameAs(player))
                Players.RemoveAt(i);
        }

        var have = Players.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var insertAt = 0;
        foreach (var player in snapshot)
        {
            if (have.Contains(player.Name))
            {
                insertAt++;
                continue;
            }

            Players.Insert(insertAt, new PlayerRowViewModel(player));
            insertAt++;
        }

        _ = HydrateFacesAsync();
    }

    private void SyncBanned()
    {
        var snapshot = _main.BannedPlayers;
        var incoming = snapshot.ToDictionary(p => p.Name, StringComparer.Ordinal);

        for (var i = BannedPlayers.Count - 1; i >= 0; i--)
        {
            var row = BannedPlayers[i];
            if (!incoming.TryGetValue(row.Name, out var player) || !row.SameAs(player))
                BannedPlayers.RemoveAt(i);
        }

        var have = BannedPlayers.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var insertAt = 0;
        foreach (var player in snapshot)
        {
            if (have.Contains(player.Name))
            {
                insertAt++;
                continue;
            }

            BannedPlayers.Insert(insertAt, new PlayerRowViewModel(player));
            insertAt++;
        }

        _ = HydrateFacesAsync();
    }

    private async Task HydrateFacesAsync()
    {
        var gen = Interlocked.Increment(ref _faceGen);
        var pending = Players.Where(p => p.HasUuid && string.IsNullOrEmpty(p.FaceDataUrl))
            .Concat(BannedPlayers.Where(p => p.HasUuid && string.IsNullOrEmpty(p.FaceDataUrl)))
            .ToList();
        foreach (var row in pending)
        {
            if (_disposed || gen != _faceGen)
                return;

            string? url;
            try
            {
                url = await _avatars.TryGetDataUrlAsync(row.Uuid).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed || gen != _faceGen)
                return;
            if (string.IsNullOrEmpty(url))
                continue;

            await _dispatcher.InvokeAsync(() =>
            {
                if (_disposed || gen != _faceGen)
                    return;
                if (Players.Contains(row) || BannedPlayers.Contains(row))
                    row.FaceDataUrl = url;
            }).ConfigureAwait(false);
        }
    }

    public Task KickAsync(PlayerRowViewModel? row, string? reason) =>
        SendActionAsync("kick", row, reason, refreshRoster: true);

    public Task ModAsync(PlayerRowViewModel? row) =>
        SendActionAsync("op", row, reason: null, refreshRoster: false);

    public Task UnmodAsync(PlayerRowViewModel? row) =>
        SendActionAsync("deop", row, reason: null, refreshRoster: false);

    public Task BanAsync(PlayerRowViewModel? row, string? reason) =>
        SendActionAsync("ban", row, reason, refreshRoster: true);

    public Task UnbanAsync(PlayerRowViewModel? row) =>
        SendActionAsync("pardon", row, reason: null, refreshRoster: true);

    private async Task SendActionAsync(
        string verb,
        PlayerRowViewModel? row,
        string? reason,
        bool refreshRoster)
    {
        if (IsBusy || row is null)
            return;
        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        if (!MinecraftConsoleRemote.TryBuildPlayerActionCommand(verb, row.Name, reason, out var command, out var error))
        {
            StatusMessage = error ?? "Could not build that command.";
            return;
        }

        if (!MinecraftJoinable)
        {
            StatusMessage = MinecraftConsoleRemote.SendDisabledReason(
                Vm1IsRunning, MinecraftJoinable, busy: false, command);
            return;
        }

        IsBusy = true;
        StatusMessage = ActionProgressCopy(verb, row.Name);
        try
        {
            var run = await _ssh.SendMinecraftRconAsync(_config.Vm1, command).ConfigureAwait(true);
            if (!run.Succeeded)
            {
                var hint = MinecraftConsoleRemote.OperatorHintFromRcon(run);
                StatusMessage = string.IsNullOrWhiteSpace(hint) ? "Command failed." : hint;
                return;
            }

            StatusMessage = ActionDoneCopy(verb, row.Name);
            if (refreshRoster)
                await _main.RefreshPlayersPinNowAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ActionProgressCopy(string verb, string name) => verb switch
    {
        "kick" => "Kicking " + name + "…",
        "op" => "Modding " + name + "…",
        "deop" => "Unmodding " + name + "…",
        "ban" => "Banning " + name + "…",
        "pardon" => "Unbanning " + name + "…",
        _ => "Sending…",
    };

    private static string ActionDoneCopy(string verb, string name) => verb switch
    {
        "kick" => "Kicked " + name + ".",
        "op" => "Modded " + name + ".",
        "deop" => "Unmodded " + name + ".",
        "ban" => "Banned " + name + " in Minecraft. Who can join was not changed.",
        "pardon" => "Unbanned " + name + ".",
        _ => "Done.",
    };
}
