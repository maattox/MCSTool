using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

public enum CapacityWaitChoice
{
    Dismissed,
    RetryNow,
    AutoRetry,
}

/// <summary>
/// Nine-step Setup wizard (Always Free → OCI → compartment → email → SSH →
/// Vanilla → EULA → Auth Token → summary). No Window Host — pickers/clipboard/
/// dialogs/clock via B3 interfaces. Does not tofu apply unless the operator
/// clicks Deploy; agents use <c>MCMANAGER_TOFU_DRY_RUN=1</c>.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    public const string AlwaysFreeDocsUrl =
        "https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute";

    public const string MinecraftEulaUrl = "https://aka.ms/MinecraftEULA";

    public const string CapacityWaitExplanation =
        "Always Free A1 Flex host capacity is unavailable in this region right now. VM1 was not created.\n\n"
        + "Other Always Free resources from this Setup (compartment, VCN, door Micro, reserved IP, IAM) may already exist. Retry reuses them; it does not start from scratch.\n\n"
        + "Try again now, or auto-retry every 5 minutes while Setup stays open. Auto-retry checks capacity first and stays silent on later failures.\n\n"
        + "Close returns to Setup so you can pause later or resume another time.";

    private static readonly TimeSpan LogFlushPeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CapacityPollPeriod = TimeSpan.FromMinutes(5);

    private readonly IFilePicker _picker;
    private readonly IClipboard _clipboard;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly MojangVersionCatalog _catalog = new();
    private readonly List<OciConfigProfile> _profiles = [];
    private readonly List<string> _versionIds = [];
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();

    private MojangVersionManifest? _manifest;
    private CancellationTokenSource? _logFlushCts;
    private CancellationTokenSource? _capacityCts;
    private TaskCompletionSource<CapacityWaitChoice>? _capacityChoice;
    private string _functionImage = "";
    private string _resumeMinecraftVersion = "";
    private bool _navReady;

    public IReadOnlyList<OciConfigProfile> Profiles => _profiles;

    public IReadOnlyList<string> VersionIds => _versionIds;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _alwaysFreeConfirmed;

    [ObservableProperty]
    private bool _residualChargeDisclosed;

    [ObservableProperty]
    private bool _capacityWaitConsent;

    [ObservableProperty]
    private string _ociProfile = "DEFAULT";

    [ObservableProperty]
    private string _ociRegion = "";

    [ObservableProperty]
    private bool _createCompartment = true;

    [ObservableProperty]
    private string _compartmentName = "mcmgr";

    [ObservableProperty]
    private string _existingCompartmentId = "";

    [ObservableProperty]
    private string _alertEmail = "";

    [ObservableProperty]
    private bool _sshGenerateMode = true;

    [ObservableProperty]
    private string _sshPublicKeyPath = "";

    [ObservableProperty]
    private string _sshPublicKey = "";

    [ObservableProperty]
    private string _sshFingerprint = "";

    [ObservableProperty]
    private bool _vanillaConfirmed;

    [ObservableProperty]
    private bool _includeSnapshots;

    [ObservableProperty]
    private string _minecraftVersion = "";

    [ObservableProperty]
    private string _versionCatalogNotes = "Loading Minecraft versions…";

    [ObservableProperty]
    private bool _eulaAccepted;

    [ObservableProperty]
    private string _authTokenInput = "";

    [ObservableProperty]
    private bool _authTokenStored;

    [ObservableProperty]
    private string _adminCidr = "";

    [ObservableProperty]
    private string _adminMinecraftUsername = "";

    [ObservableProperty]
    private string _applyStage = SetupApplyStage.NotStarted;

    [ObservableProperty]
    private string _deployLog = "";

    [ObservableProperty]
    private bool _capacityWaiting;

    [ObservableProperty]
    private bool _isPollingCapacity;

    [ObservableProperty]
    private bool _createResourcesConfirmed;

    [ObservableProperty]
    private bool _replaceConfigConfirmed;

    [ObservableProperty]
    private bool _isDeployLocked;

    [ObservableProperty]
    private double _deployProgressPercent;

    [ObservableProperty]
    private string _deployProgressCaption = "";

    [ObservableProperty]
    private bool _capacityDialogOpen;

    public bool HasExistingManageConfig { get; }

    public bool IsTofuDryRun { get; } = ProductPaths.IsTofuDryRun();

    public string AuthTokenStoredDisplay =>
        AuthTokenStored
            ? "Stored in Credential Manager: yes (McManager/ocir)"
            : "Stored in Credential Manager: no";

    public SetupWizardViewModel(
        IFilePicker picker,
        IClipboard clipboard,
        IUiClock clock,
        IUiDispatcher dispatcher)
    {
        _picker = picker;
        _clipboard = clipboard;
        _clock = clock;
        _dispatcher = dispatcher;
        LoadFrom(SetupWizardStore.LoadOrNew());
        LoadProfiles();
        AuthTokenStored = AuthTokenStored || WindowsCredentialStore.Exists();
        HasExistingManageConfig = LocalConfigStore.HasManageConfig();
        _navReady = true;
    }

    public bool CanGoBack => CurrentStep > 0 && !IsBusy && !IsDeployLocked;

    public bool IsLastStep => CurrentStep >= SetupWizardState.StepCount - 1;

    public bool CanGoNext =>
        CurrentStep < SetupWizardState.StepCount - 1
        && StepIsValid(CurrentStep)
        && !IsDeployLocked;

    public bool ShowDeployButton => IsLastStep && !CapacityWaiting;

    public bool ShowCapacityOptionsButton =>
        IsLastStep && CapacityWaiting && !IsPollingCapacity && !IsBusy;

    public bool CanDeploy =>
        ShowDeployButton
        && EulaAccepted
        && TfvarsWriter.NormalizeAdminCidr(AdminCidr) is not null
        && MinecraftUsername.IsMissingOrValid(AdminMinecraftUsername)
        && !IsBusy
        && !IsDeployLocked
        && CreateResourcesConfirmed
        && (!ShowReplaceConfigConfirm || ReplaceConfigConfirmed);

    public bool CanRetryDeploy =>
        CapacityWaiting
        && !IsBusy
        && EulaAccepted
        && TfvarsWriter.NormalizeAdminCidr(AdminCidr) is not null
        && MinecraftUsername.IsMissingOrValid(AdminMinecraftUsername);

    public bool CanCloseWizard => !IsBusy;

    public bool CanMutateWizard => !IsBusy && !IsDeployLocked;

    public bool ShowDeployProgress =>
        IsLastStep && (IsBusy || IsDeployLocked || DeployProgressPercent > 0);

    public string DeployProgressPercentDisplay => $"{(int)Math.Round(DeployProgressPercent)}%";

    public string DeployToolTip =>
        IsDeployLocked
            ? "Deploy already started or finished on this page. Close, then use Advanced → Deploy / repair infrastructure to resume or re-run."
            : "Creates Always Free resources. State is under LocalAppData, not the repo terraform.tfvars.";

    public bool ShowReplaceConfigConfirm => HasExistingManageConfig && !IsTofuDryRun;

    public string ProfileDetailsText
    {
        get
        {
            var selected = _profiles.FirstOrDefault(p =>
                string.Equals(p.Name, OciProfile, StringComparison.OrdinalIgnoreCase));
            return selected?.DetailsText
                ?? "Select a profile to confirm region, tenancy, and user from ~/.oci/config.";
        }
    }

    public string CreateResourcesConfirmText =>
        IsTofuDryRun
            ? "I understand this is a dry-run (no Oracle resources and config.local.json will not be written)."
            : "Create Always Free game VM + doorbell VM + reserved play IP in the selected tenancy.";

    public string AutoRetryBannerText =>
        "Auto-retrying every 5 minutes until A1 capacity is available. Failures stay silent. Use Pause auto-retry to stop.";

    public bool IsStepAlwaysFree => CurrentStep == 0;
    public bool IsStepOci => CurrentStep == 1;
    public bool IsStepCompartment => CurrentStep == 2;
    public bool IsStepAlertEmail => CurrentStep == 3;
    public bool IsStepSsh => CurrentStep == 4;
    public bool IsStepGame => CurrentStep == 5;
    public bool IsStepEula => CurrentStep == 6;
    public bool IsStepAuthToken => CurrentStep == 7;
    public bool IsStepSummary => CurrentStep == 8;

    public bool UseExistingCompartment
    {
        get => !CreateCompartment;
        set => CreateCompartment = !value;
    }

    public bool SshImportMode
    {
        get => !SshGenerateMode;
        set => SshGenerateMode = !value;
    }

    public string StepTitle => CurrentStep switch
    {
        0 => "Always Free",
        1 => "Oracle Cloud profile",
        2 => "Compartment",
        3 => "Budget alert email",
        4 => "SSH key",
        5 => "Minecraft (Vanilla)",
        6 => "Mojang EULA",
        7 => "Optional Auth Token",
        8 => "Review and deploy",
        _ => "Setup",
    };

    public string StepSubtitle => $"Step {CurrentStep + 1} of {SetupWizardState.StepCount}";

    public string PlanSummaryText => InfraPlanSummary.Build(ToState());

    public async Task InitializeAsync()
    {
        await LoadVersionsAsync().ConfigureAwait(true);
        await DetectAdminIpAsync().ConfigureAwait(true);
    }

    public void Back()
    {
        if (!CanGoBack)
            return;
        CurrentStep--;
        Persist();
    }

    public async Task NextAsync()
    {
        if (!CanGoNext)
            return;

        if (CurrentStep == 7)
            TryStoreAuthToken();

        CurrentStep++;
        Persist();
        await Task.CompletedTask;
    }

    public void Persist()
    {
        var saved = SetupWizardStore.Save(ToState());
        if (!saved.Succeeded)
            StatusMessage = saved.Error ?? "Failed to save resume state.";
    }

    public void PrepareToClose()
    {
        StopCapacityPoll();
        StopLogFlushTimer();
        CapacityDialogOpen = false;
        _capacityChoice?.TrySetResult(CapacityWaitChoice.Dismissed);
        Persist();
    }

    public void OpenAlwaysFreeDocs() => OpenUrl(AlwaysFreeDocsUrl);

    public void OpenEula() => OpenUrl(MinecraftEulaUrl);

    public async Task GenerateSshKeyAsync()
    {
        if (IsBusy || IsDeployLocked)
            return;

        IsBusy = true;
        StatusMessage = "Generating ed25519 key…";
        try
        {
            var result = await SshKeyHelper.GenerateEd25519Async().ConfigureAwait(true);
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "SSH generate failed.";
                return;
            }

            SshGenerateMode = true;
            ApplySsh(result.Value);
            StatusMessage = $"Created {result.Value.Path} (private key stays on disk).";
            Persist();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportSshKeyAsync()
    {
        if (IsDeployLocked)
            return;

        var path = await _picker.OpenFileAsync(new FilePickRequest
        {
            Title = "Import OpenSSH public key",
            Filters =
            [
                new FileTypeFilter("Public keys", ".pub"),
                new FileTypeFilter("All files", ".*"),
            ],
        }).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
            return;

        var imported = SshKeyHelper.ImportPublicKey(path);
        if (!imported.Succeeded || imported.Value is null)
        {
            StatusMessage = imported.Error ?? "Import failed.";
            return;
        }

        SshGenerateMode = false;
        ApplySsh(imported.Value);
        StatusMessage = $"Imported {imported.Value.Path}";
        Persist();
    }

    public void StoreAuthToken()
    {
        TryStoreAuthToken();
        Persist();
    }

    public void ClearAuthToken()
    {
        var deleted = WindowsCredentialStore.DeleteOcirToken();
        AuthTokenInput = "";
        AuthTokenStored = WindowsCredentialStore.Exists();
        StatusMessage = deleted.Succeeded
            ? "Removed McManager/ocir from Credential Manager."
            : deleted.Error ?? "Could not delete stored token.";
        Persist();
    }

    /// <summary>Runs OpenTofu apply + bootstrap. Dry-run if MCMANAGER_TOFU_DRY_RUN=1.</summary>
    public Task DeployAsync()
    {
        if (!CanDeploy)
            return Task.CompletedTask;
        return RunDeployPipelineAsync();
    }

    public Task RetryDeployAsync()
    {
        if (!CanRetryDeploy)
            return Task.CompletedTask;
        return RunDeployPipelineAsync();
    }

    public void StartCapacityPoll()
    {
        if (IsPollingCapacity)
            return;
        IsPollingCapacity = true;
        StatusMessage = "Auto-retrying every 5 minutes.";
        QueueLog("Auto-retry armed: apply every 5 minutes (silent on capacity failures).");
        FlushLog();
        _capacityCts?.Cancel();
        _capacityCts?.Dispose();
        _capacityCts = new CancellationTokenSource();
        _ = RunCapacityPollLoopAsync(_capacityCts.Token);
    }

    public void StopCapacityPoll()
    {
        _capacityCts?.Cancel();
        _capacityCts?.Dispose();
        _capacityCts = null;
        if (IsPollingCapacity)
            StatusMessage = "Auto-retry paused. Use Retry options to try again or resume.";
        IsPollingCapacity = false;
    }

    public Task ShowCapacityOptionsAsync()
    {
        if (IsBusy)
            return Task.CompletedTask;
        return PromptCapacityWaitAsync();
    }

    public void CompleteCapacityDialog(CapacityWaitChoice choice)
    {
        CapacityDialogOpen = false;
        _capacityChoice?.TrySetResult(choice);
    }

    public async Task CopyPlanSummaryAsync() =>
        await CopyToClipboardAsync(PlanSummaryText, "Copied plan summary.").ConfigureAwait(true);

    public async Task CopyDeployLogAsync()
    {
        FlushLog();
        await CopyToClipboardAsync(
            string.IsNullOrWhiteSpace(DeployLog) ? "(empty)" : DeployLog,
            "Copied deploy log.").ConfigureAwait(true);
    }

    private async Task CopyToClipboardAsync(string text, string okMessage)
    {
        try
        {
            await _clipboard.SetTextAsync(text).ConfigureAwait(true);
            StatusMessage = okMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = "Clipboard unavailable: " + ex.Message;
        }
    }

    private async Task RunDeployPipelineAsync()
    {
        var promptCapacity = false;
        IsDeployLocked = true;
        IsBusy = true;
        StatusMessage = IsTofuDryRun ? "Dry-run deploy (no Oracle Cloud)…" : "Deploying…";
        ApplyProgress(SetupApplyStage.Update(SetupApplyStage.NotStarted, "Starting…"));
        StartLogFlushTimer();
        try
        {
            var log = new BufferedProgress(QueueLog);
            var progress = new Progress<SetupProgressUpdate>(ApplyProgress);
            var orch = new SetupDeployOrchestrator();
            var state = ToState();
            var result = await Task.Run(async () =>
                    await orch.RunAsync(state, log, CancellationToken.None, progress).ConfigureAwait(false))
                .ConfigureAwait(true);
            ApplyStage = result.Stage;
            var saved = SetupWizardStore.LoadOrNew();
            _functionImage = saved.FunctionImage ?? _functionImage;
            FlushLog();
            if (result.CapacityWait)
            {
                CapacityWaiting = true;
                Persist();
                if (IsPollingCapacity)
                    StatusMessage = "Auto-retrying every 5 minutes.";
                else
                {
                    StatusMessage = "Always Free A1 capacity is unavailable.";
                    promptCapacity = true;
                }
            }
            else
            {
                if (result.Succeeded)
                    CapacityWaiting = false;

                if (IsPollingCapacity && result.Succeeded)
                    StopCapacityPoll();

                if (!result.Succeeded)
                    CapacityWaiting = false;

                StatusMessage = ShortStatus(result.Message);
                Persist();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ShortStatus("Deploy failed: " + ex.Message);
            QueueLog(ex.ToString());
            FlushLog();
        }
        finally
        {
            StopLogFlushTimer();
            IsBusy = false;
        }

        if (promptCapacity)
            await PromptCapacityWaitAsync().ConfigureAwait(true);
    }

    private void ApplyProgress(SetupProgressUpdate update)
    {
        void Apply()
        {
            DeployProgressPercent = update.Percent;
            DeployProgressCaption = string.IsNullOrWhiteSpace(update.Caption)
                ? SetupApplyStage.DisplayName(update.Stage)
                : update.Caption;
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _ = _dispatcher.InvokeAsync(Apply);
    }

    private async Task PromptCapacityWaitAsync()
    {
        _capacityChoice = new TaskCompletionSource<CapacityWaitChoice>();
        CapacityDialogOpen = true;
        var choice = await _capacityChoice.Task.ConfigureAwait(true);
        CapacityDialogOpen = false;
        if (choice == CapacityWaitChoice.RetryNow)
            await RunDeployPipelineAsync().ConfigureAwait(true);
        else if (choice == CapacityWaitChoice.AutoRetry)
            StartCapacityPoll();
    }

    private void StartLogFlushTimer()
    {
        if (_logFlushCts is not null)
            return;
        _logFlushCts = new CancellationTokenSource();
        _ = RunLogFlushLoopAsync(_logFlushCts.Token);
    }

    private void StopLogFlushTimer()
    {
        FlushLog();
        _logFlushCts?.Cancel();
        _logFlushCts?.Dispose();
        _logFlushCts = null;
    }

    private async Task RunLogFlushLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = _clock.CreatePeriodicTimer(LogFlushPeriod);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(FlushLog, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
    }

    private async Task RunCapacityPollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = _clock.CreatePeriodicTimer(CapacityPollPeriod);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (IsBusy || !IsPollingCapacity)
                    continue;
                await _dispatcher.InvokeAsync(
                    () => RunDeployPipelineAsync(),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // paused or closed
        }
    }

    private void QueueLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        lock (_logLock)
        {
            foreach (var raw in line.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                _logBuffer.Append('[').Append(stamp).Append("] ").AppendLine(raw.TrimEnd());
            }
        }
    }

    private void FlushLog()
    {
        string chunk;
        lock (_logLock)
        {
            if (_logBuffer.Length == 0)
                return;
            chunk = _logBuffer.ToString();
            _logBuffer.Clear();
        }

        if (DeployLog.Length == 0)
            DeployLog = chunk.TrimEnd();
        else
            DeployLog += chunk;

        if (DeployLog.Length > 80_000)
            DeployLog = DeployLog[^60_000..];
    }

    private static string ShortStatus(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";
        var first = message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        return first.Length <= 180 ? first : first[..177] + "…";
    }

    private sealed class BufferedProgress : IProgress<string>
    {
        private readonly Action<string> _append;

        public BufferedProgress(Action<string> append) => _append = append;

        public void Report(string value) => _append(value);
    }

    private async Task DetectAdminIpAsync()
    {
        if (!string.IsNullOrWhiteSpace(AdminCidr))
            return;
        var detected = await PublicIpDetector.FetchPublicIpAsync().ConfigureAwait(true);
        if (detected.Succeeded && !string.IsNullOrWhiteSpace(detected.Value))
        {
            AdminCidr = detected.Value + "/32";
            Persist();
        }
        else
        {
            StatusMessage = detected.Error ?? "Could not detect public IP. Enter it on the summary step.";
        }
    }

    private void TryStoreAuthToken()
    {
        if (string.IsNullOrWhiteSpace(AuthTokenInput))
            return;

        var saved = WindowsCredentialStore.SaveOcirToken(AuthTokenInput);
        if (!saved.Succeeded)
        {
            StatusMessage = saved.Error ?? "Failed to store Auth Token.";
            return;
        }

        AuthTokenInput = "";
        AuthTokenStored = true;
        StatusMessage = "Auth Token stored in Windows Credential Manager (McManager/ocir). Not written to wizard JSON.";
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var result = await _catalog.LoadAsync().ConfigureAwait(true);
            _manifest = result.Manifest;
            VersionCatalogNotes = result.Notes;
            RebuildVersionList(keepSelection: true);
        }
        catch (Exception ex)
        {
            VersionCatalogNotes = $"Version catalog failed: {ex.Message}";
        }
    }

    private void RebuildVersionList(bool keepSelection)
    {
        if (_manifest is null)
            return;

        var previous = keepSelection
            ? (string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion)
            : "";
        var filtered = MojangVersionCatalog.Filter(_manifest, IncludeSnapshots);
        _versionIds.Clear();
        foreach (var v in filtered)
            _versionIds.Add(v.Id);
        OnPropertyChanged(nameof(VersionIds));

        var target = !string.IsNullOrWhiteSpace(previous) && _versionIds.Contains(previous)
            ? previous
            : MojangVersionCatalog.DefaultVersionId(_manifest);
        MinecraftVersion = target ?? "";
        if (!string.IsNullOrWhiteSpace(MinecraftVersion))
            _resumeMinecraftVersion = MinecraftVersion;
        Persist();
    }

    private void LoadProfiles()
    {
        _profiles.Clear();
        foreach (var p in OciConfigProfiles.List())
            _profiles.Add(p);
        OnPropertyChanged(nameof(Profiles));

        var selected = _profiles.FirstOrDefault(p =>
                string.Equals(p.Name, OciProfile, StringComparison.OrdinalIgnoreCase))
            ?? _profiles.FirstOrDefault();

        if (selected is not null)
        {
            OciProfile = selected.Name;
            OciRegion = selected.Region;
        }

        OnPropertyChanged(nameof(ProfileDetailsText));
    }

    private void ApplySsh(SshPublicKeyInfo info)
    {
        SshPublicKeyPath = info.Path;
        SshPublicKey = info.PublicKeyLine;
        SshFingerprint = info.Fingerprint;
    }

    private void LoadFrom(SetupWizardState state)
    {
        CurrentStep = state.CurrentStep;
        AlwaysFreeConfirmed = state.AlwaysFreeConfirmed;
        ResidualChargeDisclosed = state.ResidualChargeDisclosed;
        CapacityWaitConsent = state.CapacityWaitConsent;
        OciProfile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile;
        OciRegion = state.OciRegion;
        CreateCompartment = state.CreateCompartment;
        CompartmentName = string.IsNullOrWhiteSpace(state.CompartmentName) ? "mcmgr" : state.CompartmentName;
        ExistingCompartmentId = state.ExistingCompartmentId;
        AlertEmail = state.AlertEmail;
        SshGenerateMode = !string.Equals(state.SshMode, "import", StringComparison.OrdinalIgnoreCase);
        SshPublicKeyPath = state.SshPublicKeyPath;
        SshPublicKey = state.SshPublicKey;
        SshFingerprint = state.SshFingerprint;
        VanillaConfirmed = state.VanillaConfirmed;
        IncludeSnapshots = state.IncludeSnapshots;
        MinecraftVersion = state.MinecraftVersion;
        _resumeMinecraftVersion = state.MinecraftVersion;
        EulaAccepted = state.EulaAccepted;
        AuthTokenStored = state.AuthTokenStored;
        AdminCidr = state.AdminCidr;
        AdminMinecraftUsername = state.AdminMinecraftUsername;
        ApplyStage = string.IsNullOrWhiteSpace(state.ApplyStage)
            ? SetupApplyStage.NotStarted
            : state.ApplyStage;
        _functionImage = state.FunctionImage ?? "";
    }

    public SetupWizardState ToState() => new()
    {
        CurrentStep = CurrentStep,
        AlwaysFreeConfirmed = AlwaysFreeConfirmed,
        ResidualChargeDisclosed = ResidualChargeDisclosed,
        CapacityWaitConsent = CapacityWaitConsent,
        OciProfile = OciProfile,
        OciRegion = OciRegion,
        CreateCompartment = CreateCompartment,
        CompartmentName = CompartmentName,
        ExistingCompartmentId = ExistingCompartmentId,
        AlertEmail = AlertEmail,
        SshMode = SshGenerateMode ? "generate" : "import",
        SshPublicKeyPath = SshPublicKeyPath,
        SshPublicKey = SshPublicKey,
        SshFingerprint = SshFingerprint,
        VanillaConfirmed = VanillaConfirmed,
        IncludeSnapshots = IncludeSnapshots,
        MinecraftVersion = string.IsNullOrWhiteSpace(MinecraftVersion)
            ? _resumeMinecraftVersion
            : MinecraftVersion,
        EulaAccepted = EulaAccepted,
        AuthTokenStored = AuthTokenStored,
        AdminCidr = AdminCidr,
        AdminMinecraftUsername = AdminMinecraftUsername,
        ApplyStage = ApplyStage,
        FunctionImage = _functionImage,
    };

    private bool StepIsValid(int step) => step switch
    {
        0 => AlwaysFreeConfirmed && ResidualChargeDisclosed && CapacityWaitConsent,
        1 => !string.IsNullOrWhiteSpace(OciProfile) && !string.IsNullOrWhiteSpace(OciRegion),
        2 => CreateCompartment
            ? !string.IsNullOrWhiteSpace(CompartmentName)
            : ExistingCompartmentId.Trim().StartsWith("ocid1.compartment.", StringComparison.Ordinal),
        3 => AlertEmail.Contains('@', StringComparison.Ordinal),
        4 => SshKeyHelper.LooksLikePublicKey(SshPublicKey),
        5 => VanillaConfirmed && !string.IsNullOrWhiteSpace(
            string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion),
        6 => EulaAccepted,
        7 => true,
        _ => false,
    };

    partial void OnOciProfileChanged(string value)
    {
        var selected = _profiles.FirstOrDefault(p =>
            string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
            return;
        OciRegion = selected.Region;
        OnPropertyChanged(nameof(ProfileDetailsText));
    }

    partial void OnCreateCompartmentChanged(bool value) => OnPropertyChanged(nameof(UseExistingCompartment));

    partial void OnSshGenerateModeChanged(bool value) => OnPropertyChanged(nameof(SshImportMode));

    partial void OnAuthTokenStoredChanged(bool value) => OnPropertyChanged(nameof(AuthTokenStoredDisplay));

    partial void OnMinecraftVersionChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        _resumeMinecraftVersion = value;
    }

    partial void OnIncludeSnapshotsChanged(bool value) => RebuildVersionList(keepSelection: true);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(CanGoNext):
            case nameof(CanGoBack):
            case nameof(IsLastStep):
            case nameof(StepTitle):
            case nameof(StepSubtitle):
            case nameof(PlanSummaryText):
            case nameof(IsStepAlwaysFree):
            case nameof(IsStepOci):
            case nameof(IsStepCompartment):
            case nameof(IsStepAlertEmail):
            case nameof(IsStepSsh):
            case nameof(IsStepGame):
            case nameof(IsStepEula):
            case nameof(IsStepAuthToken):
            case nameof(IsStepSummary):
            case nameof(UseExistingCompartment):
            case nameof(SshImportMode):
            case nameof(AuthTokenStoredDisplay):
            case nameof(StatusMessage):
            case nameof(AuthTokenInput):
            case nameof(DeployLog):
            case nameof(CanDeploy):
            case nameof(CanRetryDeploy):
            case nameof(ShowDeployButton):
            case nameof(ShowCapacityOptionsButton):
            case nameof(ShowReplaceConfigConfirm):
            case nameof(ShowDeployProgress):
            case nameof(CanCloseWizard):
            case nameof(CanMutateWizard):
            case nameof(DeployToolTip):
            case nameof(DeployProgressPercentDisplay):
            case nameof(ProfileDetailsText):
            case nameof(CreateResourcesConfirmText):
            case nameof(Profiles):
            case nameof(VersionIds):
            case nameof(CapacityDialogOpen):
                return;
        }

        if (!_navReady)
            return;

        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepSubtitle));
        OnPropertyChanged(nameof(PlanSummaryText));
        OnPropertyChanged(nameof(IsStepAlwaysFree));
        OnPropertyChanged(nameof(IsStepOci));
        OnPropertyChanged(nameof(IsStepCompartment));
        OnPropertyChanged(nameof(IsStepAlertEmail));
        OnPropertyChanged(nameof(IsStepSsh));
        OnPropertyChanged(nameof(IsStepGame));
        OnPropertyChanged(nameof(IsStepEula));
        OnPropertyChanged(nameof(IsStepAuthToken));
        OnPropertyChanged(nameof(IsStepSummary));
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanRetryDeploy));
        OnPropertyChanged(nameof(ShowDeployButton));
        OnPropertyChanged(nameof(ShowCapacityOptionsButton));
        OnPropertyChanged(nameof(ShowReplaceConfigConfirm));
        OnPropertyChanged(nameof(ShowDeployProgress));
        OnPropertyChanged(nameof(CanCloseWizard));
        OnPropertyChanged(nameof(CanMutateWizard));
        OnPropertyChanged(nameof(DeployToolTip));
        OnPropertyChanged(nameof(DeployProgressPercentDisplay));
        OnPropertyChanged(nameof(ProfileDetailsText));
        OnPropertyChanged(nameof(CreateResourcesConfirmText));
        OnPropertyChanged(nameof(UseExistingCompartment));
        OnPropertyChanged(nameof(SshImportMode));
        OnPropertyChanged(nameof(AuthTokenStoredDisplay));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }
}
