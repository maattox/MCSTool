using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
/// game (Vanilla Default/Paper or Modded pack file) → EULA → Auth Token → summary).
/// No Window Host — pickers/clipboard/dialogs/clock via B3 interfaces. Does not
/// tofu apply unless the operator clicks Deploy; agents use <c>MCMANAGER_TOFU_DRY_RUN=1</c>.
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

    public const string DeployDurationHint =
        "Creating the cloud computers and installing Minecraft often takes a long time. Leave this window open until it finishes.";

    public const long PackDropMaxBytes = 512L * 1024 * 1024;

    private static readonly TimeSpan LogFlushPeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ElapsedTickPeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CapacityPollPeriod = TimeSpan.FromMinutes(5);

    private readonly IFilePicker _picker;
    private readonly IClipboard _clipboard;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly MojangVersionCatalog _catalog = new();
    private readonly PaperFillV3Client _paperCatalog = new();
    private readonly List<OciConfigProfile> _profiles = [];
    private readonly List<string> _versionIds = [];
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();

    private MojangVersionManifest? _manifest;
    private PaperFillProject? _paperProject;
    private string _mojangCatalogNotes = "";
    private string _paperCatalogNotes = "";
    private CancellationTokenSource? _logFlushCts;
    private CancellationTokenSource? _elapsedCts;
    private CancellationTokenSource? _capacityCts;
    private DateTimeOffset? _elapsedRunningSince;
    private TimeSpan _elapsedAccumulated;
    private bool _elapsedStarted;
    private string _progressStage = SetupApplyStage.NotStarted;
    private bool _progressStageComplete;
    private DateTimeOffset? _progressStageStartedAt;
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
    private string _serverType = SetupServerType.Vanilla;

    [ObservableProperty]
    private string _vanillaFlavor = SetupVanillaFlavor.Default;

    [ObservableProperty]
    private bool _includeSnapshots;

    [ObservableProperty]
    private string _minecraftVersion = "";

    [ObservableProperty]
    private string _versionCatalogNotes = "Loading Minecraft versions…";

    [ObservableProperty]
    private bool _isAnalyzingPack;

    [ObservableProperty]
    private string _packAnalyzeCaption = "";

    [ObservableProperty]
    private string _packPath = "";

    [ObservableProperty]
    private string _packKind = "";

    [ObservableProperty]
    private string _packName = "";

    [ObservableProperty]
    private string _packVersionId = "";

    [ObservableProperty]
    private string _packLoader = "";

    [ObservableProperty]
    private string _packLoaderVersion = "";

    [ObservableProperty]
    private string _packSummary = "";

    [ObservableProperty]
    private string _packBlockReason = "";

    [ObservableProperty]
    private bool _packCanContinue;

    [ObservableProperty]
    private bool _packConfirmed;

    [ObservableProperty]
    private bool _clientPackAcknowledged;

    [ObservableProperty]
    private bool _eulaAccepted;

    [ObservableProperty]
    private string _authTokenInput = "";

    [ObservableProperty]
    private bool _authTokenStored;

    [ObservableProperty]
    private string _adminCidr = "";

    [ObservableProperty]
    private int _vm1Ocpus = Vm1ShapeChoice.DefaultOcpus;

    [ObservableProperty]
    private int _vm1MemoryGb = Vm1ShapeChoice.DefaultMemoryGb;

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
        && Vm1ShapeChoice.IsAllowed(Vm1Ocpus, Vm1MemoryGb)
        && !IsBusy
        && !IsDeployLocked
        && CreateResourcesConfirmed
        && (!ShowReplaceConfigConfirm || ReplaceConfigConfirmed);

    public bool CanRetryDeploy =>
        CapacityWaiting
        && !IsBusy
        && EulaAccepted
        && TfvarsWriter.NormalizeAdminCidr(AdminCidr) is not null
        && Vm1ShapeChoice.IsAllowed(Vm1Ocpus, Vm1MemoryGb);

    public bool CanCloseWizard => !IsBusy;

    public bool CanMutateWizard => !IsBusy && !IsDeployLocked;

    public bool ShowDeployProgress =>
        IsLastStep && (IsBusy || IsDeployLocked || DeployProgressPercent > 0);

    public string DeployProgressPercentDisplay => $"{(int)Math.Round(DeployProgressPercent)}%";

    public bool ShowDeployElapsed => _elapsedStarted;

    public string DeployElapsedDisplay => FormatDeployElapsed(CurrentDeployElapsed());

    public bool ShowDeployRemaining =>
        IsBusy
        && _elapsedStarted
        && !IsTofuDryRun
        && !CapacityWaiting
        && !(_progressStageComplete
             && string.Equals(_progressStage, SetupApplyStage.ConfigWritten, StringComparison.Ordinal));

    public string DeployRemainingDisplay
    {
        get
        {
            var remaining = SetupApplyStage.EstimateRemaining(
                _progressStage,
                CurrentStageElapsed(),
                _progressStageComplete);
            return SetupApplyStage.FormatRemaining(remaining);
        }
    }

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
            : $"Create Always Free game VM ({Vm1ShapeChoice.Format(Vm1Ocpus, Vm1MemoryGb)}) + doorbell VM + reserved play IP in the selected tenancy.";

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

    public bool Vm1ShapeIsDefault =>
        Vm1Ocpus == Vm1ShapeChoice.DefaultOcpus && Vm1MemoryGb == Vm1ShapeChoice.DefaultMemoryGb;

    public bool Vm1ShapeIsSmaller =>
        Vm1Ocpus == Vm1ShapeChoice.SmallerOcpus && Vm1MemoryGb == Vm1ShapeChoice.SmallerMemoryGb;

    public void SelectDefaultVm1Shape()
    {
        Vm1Ocpus = Vm1ShapeChoice.DefaultOcpus;
        Vm1MemoryGb = Vm1ShapeChoice.DefaultMemoryGb;
    }

    public void SelectSmallerVm1Shape()
    {
        Vm1Ocpus = Vm1ShapeChoice.SmallerOcpus;
        Vm1MemoryGb = Vm1ShapeChoice.SmallerMemoryGb;
    }

    public bool VanillaFlavorIsDefault =>
        !SetupVanillaFlavor.IsOptimized(VanillaFlavor);

    public bool VanillaFlavorIsOptimized =>
        SetupVanillaFlavor.IsOptimized(VanillaFlavor);

    public bool ServerTypeIsVanilla => SetupServerType.IsVanilla(ServerType);

    public bool ServerTypeIsModded => SetupServerType.IsModded(ServerType);

    public bool ShowVanillaGameOptions => ServerTypeIsVanilla;

    public bool ShowModdedGameOptions => ServerTypeIsModded;

    public bool ShowSnapshotToggle => ServerTypeIsVanilla && VanillaFlavorIsDefault;

    public string PackFileNameDisplay =>
        string.IsNullOrWhiteSpace(PackPath) ? "" : Path.GetFileName(PackPath);

    public bool ShowPackSummary =>
        ServerTypeIsModded
        && !IsAnalyzingPack
        && (!string.IsNullOrWhiteSpace(PackSummary) || !string.IsNullOrWhiteSpace(PackBlockReason));

    public bool ShowPackConfirmChecks => ShowPackSummary && PackCanContinue;

    public string ClientPackCopy => SetupPackImport.ClientPackCopy;

    public void SelectDefaultVanilla() => VanillaFlavor = SetupVanillaFlavor.Default;

    public void SelectOptimizedVanilla() => VanillaFlavor = SetupVanillaFlavor.Optimized;

    public void SelectVanillaServer() => ServerType = SetupServerType.Vanilla;

    public void SelectModdedServer() => ServerType = SetupServerType.Modded;

    public string StepTitle => CurrentStep switch
    {
        0 => "Always Free",
        1 => "Oracle Cloud profile",
        2 => "Compartment",
        3 => "Budget alert email",
        4 => "SSH key",
        5 => "Minecraft",
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
        if (ServerTypeIsModded && !string.IsNullOrWhiteSpace(PackPath) && File.Exists(PackPath))
            await AnalyzePackPathAsync(PackPath, keepConfirm: true).ConfigureAwait(true);
        else if (ServerTypeIsModded && !string.IsNullOrWhiteSpace(PackPath) && !File.Exists(PackPath))
        {
            PackBlockReason = "The pack file is missing. Choose it again.";
            PackCanContinue = false;
            PackConfirmed = false;
        }
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
        PauseDeployElapsed();
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

    public async Task PickPackAsync()
    {
        if (IsDeployLocked || IsAnalyzingPack)
            return;

        var path = await _picker.OpenFileAsync(new FilePickRequest
        {
            Title = "Choose a modpack file (.mrpack or server-pack zip)",
            Filters =
            [
                new FileTypeFilter("Modpack archives", ".mrpack", ".zip"),
                new FileTypeFilter("Modrinth pack", ".mrpack"),
                new FileTypeFilter("Zip archives", ".zip"),
                new FileTypeFilter("All files", ".*"),
            ],
        }).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
            return;

        await AnalyzePackPathAsync(path, keepConfirm: false).ConfigureAwait(true);
    }

    public async Task ImportDroppedPackAsync(string fileName, Stream content)
    {
        if (IsDeployLocked || IsAnalyzingPack || content is null)
            return;

        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "dropped-pack.zip";

        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-setup-drop");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, safeName);
        try
        {
            await using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not save the dropped pack: " + ex.Message;
            return;
        }

        await AnalyzePackPathAsync(dest, keepConfirm: false).ConfigureAwait(true);
    }

    public void ClearPack()
    {
        if (IsDeployLocked)
            return;
        PackPath = "";
        PackKind = "";
        PackName = "";
        PackVersionId = "";
        PackLoader = "";
        PackLoaderVersion = "";
        PackSummary = "";
        PackBlockReason = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        PackAnalyzeCaption = "";
        StatusMessage = "Pack cleared. Choose a .mrpack or server-pack zip.";
        Persist();
    }

    public async Task AnalyzePackPathAsync(string path, bool keepConfirm)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        IsAnalyzingPack = true;
        PackAnalyzeCaption = "Analyzing modpack…";
        PackBlockReason = "";
        PackSummary = "";
        PackCanContinue = false;
        if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }

        StatusMessage = "Analyzing modpack…";
        try
        {
            var result = await Task.Run(() => SetupPackImport.AnalyzeFile(path)).ConfigureAwait(true);
            if (!result.Succeeded || result.Value is null)
            {
                PackPath = path;
                PackSummary = "";
                PackCanContinue = false;
                PackConfirmed = false;
                PackBlockReason = result.Error ?? "Could not analyze this file.";
                StatusMessage = PackBlockReason;
                Persist();
                return;
            }

            ApplyPackPreview(result.Value, keepConfirm);
            StatusMessage = result.Value.CanContinue
                ? "Review the pack summary, then confirm before continuing."
                : (result.Value.BlockReason ?? "This pack cannot be installed.");
            Persist();
        }
        catch (Exception ex)
        {
            PackCanContinue = false;
            PackConfirmed = false;
            PackBlockReason = "Analyze failed: " + ex.Message;
            StatusMessage = PackBlockReason;
        }
        finally
        {
            IsAnalyzingPack = false;
            PackAnalyzeCaption = "";
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

    private void ApplyPackPreview(SetupPackPreview preview, bool keepConfirm)
    {
        PackPath = preview.SourcePath;
        PackKind = preview.Kind;
        PackName = preview.PackName;
        PackVersionId = preview.VersionId ?? "";
        PackLoader = preview.Loader;
        PackLoaderVersion = preview.LoaderVersion;
        PackSummary = preview.ConfirmableSummary;
        PackBlockReason = preview.BlockReason ?? "";
        PackCanContinue = preview.CanContinue;
        if (!preview.CanContinue)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
            return;
        }

        MinecraftVersion = preview.MinecraftVersion;
        _resumeMinecraftVersion = preview.MinecraftVersion;
        if (keepConfirm && PackConfirmed && ClientPackAcknowledged)
            RetainCurrentPack();
        else if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }
    }

    private void RetainCurrentPack()
    {
        if (string.IsNullOrWhiteSpace(PackPath) || !File.Exists(PackPath) || !PackCanContinue)
            return;
        var dataDir = LocalConfigStore.TryFindDataDirectory();
        if (string.IsNullOrWhiteSpace(dataDir))
            return;
        var retained = ImportedPackArchiveStore.Retain(
            PackPath,
            PackName,
            string.IsNullOrWhiteSpace(PackVersionId) ? null : PackVersionId,
            PackLoader,
            MinecraftVersion,
            dataDir);
        if (!retained.Succeeded)
            StatusMessage = retained.Error ?? "Could not keep a local copy of the pack.";
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
        DeployProgressPercent = 0;
        _progressStage = SetupApplyStage.NotStarted;
        _progressStageComplete = false;
        _progressStageStartedAt = null;
        ApplyProgress(SetupApplyStage.Starting(SetupApplyStage.NotStarted, "Starting…"));
        BeginDeployElapsed();
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
            PauseDeployElapsed();
            IsBusy = false;
        }

        if (promptCapacity)
            await PromptCapacityWaitAsync().ConfigureAwait(true);
    }

    private void ApplyProgress(SetupProgressUpdate update)
    {
        void Apply()
        {
            _progressStage = update.Stage;
            _progressStageComplete = update.StageComplete;
            _progressStageStartedAt = _clock.UtcNow;
            var next = update.StageComplete
                ? update.Percent
                : SetupApplyStage.PercentInProgress(update.Stage, TimeSpan.Zero);
            DeployProgressPercent = Math.Max(DeployProgressPercent, next);
            DeployProgressCaption = string.IsNullOrWhiteSpace(update.Caption)
                ? SetupApplyStage.DisplayName(update.Stage)
                : update.Caption;
            NotifyDeployProgressTick();
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

    private void BeginDeployElapsed()
    {
        _elapsedStarted = true;
        _elapsedRunningSince ??= _clock.UtcNow;
        StartElapsedTicker();
        NotifyDeployProgressTick();
    }

    private void PauseDeployElapsed()
    {
        if (_elapsedRunningSince is DateTimeOffset start)
        {
            var next = _elapsedAccumulated + (_clock.UtcNow - start);
            _elapsedAccumulated = next < TimeSpan.Zero ? TimeSpan.Zero : next;
            _elapsedRunningSince = null;
        }

        StopElapsedTicker();
        NotifyDeployProgressTick();
    }

    private void StartElapsedTicker()
    {
        if (_elapsedCts is not null)
            return;
        _elapsedCts = new CancellationTokenSource();
        _ = RunElapsedTickLoopAsync(_elapsedCts.Token);
    }

    private void StopElapsedTicker()
    {
        _elapsedCts?.Cancel();
        _elapsedCts?.Dispose();
        _elapsedCts = null;
    }

    private TimeSpan CurrentDeployElapsed()
    {
        var value = _elapsedAccumulated;
        if (_elapsedRunningSince is DateTimeOffset start)
            value += _clock.UtcNow - start;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private TimeSpan CurrentStageElapsed()
    {
        if (_progressStageStartedAt is not DateTimeOffset start)
            return TimeSpan.Zero;
        var value = _clock.UtcNow - start;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    internal static string FormatDeployElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var totalSeconds = (int)Math.Floor(elapsed.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"Time elapsed: {hours}:{minutes:D2}:{seconds:D2}"
            : $"Time elapsed: {minutes}:{seconds:D2}";
    }

    private void NotifyDeployProgressTick()
    {
        if (!_progressStageComplete)
        {
            var interpolated = SetupApplyStage.PercentInProgress(_progressStage, CurrentStageElapsed());
            if (interpolated > DeployProgressPercent)
                DeployProgressPercent = interpolated;
        }

        OnPropertyChanged(nameof(ShowDeployElapsed));
        OnPropertyChanged(nameof(DeployElapsedDisplay));
        OnPropertyChanged(nameof(ShowDeployRemaining));
        OnPropertyChanged(nameof(DeployRemainingDisplay));
    }

    private async Task RunElapsedTickLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = _clock.CreatePeriodicTimer(ElapsedTickPeriod);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(NotifyDeployProgressTick, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
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
            var mojang = await _catalog.LoadAsync().ConfigureAwait(true);
            _manifest = mojang.Manifest;
            _mojangCatalogNotes = mojang.Notes;
        }
        catch (Exception ex)
        {
            _mojangCatalogNotes = $"Version catalog failed: {ex.Message}";
        }

        try
        {
            var paper = await _paperCatalog.LoadProjectCatalogAsync().ConfigureAwait(true);
            _paperProject = paper.Project;
            _paperCatalogNotes = paper.Notes;
        }
        catch (Exception ex)
        {
            _paperCatalogNotes = $"Paper version list failed: {ex.Message}";
        }

        RebuildVersionList(keepSelection: true);
    }

    private void RebuildVersionList(bool keepSelection)
    {
        if (ServerTypeIsModded)
        {
            VersionCatalogNotes = "Minecraft version comes from the pack you import.";
            OnPropertyChanged(nameof(VersionIds));
            return;
        }

        var previous = keepSelection
            ? (string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion)
            : "";
        _versionIds.Clear();

        if (VanillaFlavorIsOptimized)
        {
            VersionCatalogNotes = string.IsNullOrWhiteSpace(_paperCatalogNotes)
                ? "Paper versions (Optimized Vanilla)."
                : _paperCatalogNotes;
            if (_paperProject is null)
            {
                OnPropertyChanged(nameof(VersionIds));
                return;
            }

            foreach (var id in PaperFillV3Client.FlattenVersionIds(_paperProject))
                _versionIds.Add(id);

            var target = !string.IsNullOrWhiteSpace(previous) && _versionIds.Contains(previous)
                ? previous
                : PaperFillV3Client.DefaultVersionId(_paperProject);
            ApplyVersionSelection(target);
            return;
        }

        if (_manifest is null)
        {
            VersionCatalogNotes = string.IsNullOrWhiteSpace(_mojangCatalogNotes)
                ? "Loading Minecraft versions…"
                : _mojangCatalogNotes;
            OnPropertyChanged(nameof(VersionIds));
            return;
        }

        VersionCatalogNotes = _mojangCatalogNotes;
        var filtered = MojangVersionCatalog.Filter(_manifest, IncludeSnapshots);
        foreach (var v in filtered)
            _versionIds.Add(v.Id);

        var mojangTarget = !string.IsNullOrWhiteSpace(previous) && _versionIds.Contains(previous)
            ? previous
            : MojangVersionCatalog.DefaultVersionId(_manifest);
        ApplyVersionSelection(mojangTarget);
    }

    private void ApplyVersionSelection(string? target)
    {
        OnPropertyChanged(nameof(VersionIds));
        MinecraftVersion = target ?? "";
        if (!string.IsNullOrWhiteSpace(MinecraftVersion))
            _resumeMinecraftVersion = MinecraftVersion;
        if (_navReady)
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
        ServerType = SetupServerType.Normalize(state.ServerType);
        VanillaFlavor = SetupVanillaFlavor.Normalize(state.VanillaFlavor);
        IncludeSnapshots = state.IncludeSnapshots;
        MinecraftVersion = state.MinecraftVersion;
        _resumeMinecraftVersion = state.MinecraftVersion;
        PackPath = state.PackPath;
        PackKind = state.PackKind;
        PackName = state.PackName;
        PackVersionId = state.PackVersionId;
        PackLoader = state.PackLoader;
        PackLoaderVersion = state.PackLoaderVersion;
        PackSummary = state.PackSummary;
        PackConfirmed = state.PackConfirmed;
        ClientPackAcknowledged = state.ClientPackAcknowledged;
        EulaAccepted = state.EulaAccepted;
        AuthTokenStored = state.AuthTokenStored;
        AdminCidr = state.AdminCidr;
        var shape = Vm1ShapeChoice.Normalize(state.Vm1Ocpus, state.Vm1MemoryGb);
        Vm1Ocpus = shape.Ocpus;
        Vm1MemoryGb = shape.MemoryGb;
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
        VanillaConfirmed = true,
        ServerType = SetupServerType.Normalize(ServerType),
        VanillaFlavor = SetupVanillaFlavor.Normalize(VanillaFlavor),
        IncludeSnapshots = IncludeSnapshots,
        MinecraftVersion = string.IsNullOrWhiteSpace(MinecraftVersion)
            ? _resumeMinecraftVersion
            : MinecraftVersion,
        PackPath = PackPath,
        PackKind = PackKind,
        PackName = PackName,
        PackVersionId = PackVersionId,
        PackLoader = PackLoader,
        PackLoaderVersion = PackLoaderVersion,
        PackSummary = PackSummary,
        PackConfirmed = PackConfirmed,
        ClientPackAcknowledged = ClientPackAcknowledged,
        EulaAccepted = EulaAccepted,
        AuthTokenStored = AuthTokenStored,
        AdminCidr = AdminCidr,
        Vm1Ocpus = Vm1ShapeChoice.Normalize(Vm1Ocpus, Vm1MemoryGb).Ocpus,
        Vm1MemoryGb = Vm1ShapeChoice.Normalize(Vm1Ocpus, Vm1MemoryGb).MemoryGb,
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
        5 => SetupServerType.IsModded(ServerType)
            ? PackConfirmed
                && ClientPackAcknowledged
                && PackCanContinue
                && !string.IsNullOrWhiteSpace(
                    string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion)
            : !string.IsNullOrWhiteSpace(
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

    partial void OnVanillaFlavorChanged(string value)
    {
        VanillaConfirmed = true;
        OnPropertyChanged(nameof(VanillaFlavorIsDefault));
        OnPropertyChanged(nameof(VanillaFlavorIsOptimized));
        OnPropertyChanged(nameof(ShowSnapshotToggle));
        if (_navReady && ServerTypeIsVanilla)
            RebuildVersionList(keepSelection: true);
    }

    partial void OnServerTypeChanged(string value)
    {
        OnPropertyChanged(nameof(ServerTypeIsVanilla));
        OnPropertyChanged(nameof(ServerTypeIsModded));
        OnPropertyChanged(nameof(ShowVanillaGameOptions));
        OnPropertyChanged(nameof(ShowModdedGameOptions));
        OnPropertyChanged(nameof(ShowSnapshotToggle));
        OnPropertyChanged(nameof(ShowPackSummary));
        OnPropertyChanged(nameof(ShowPackConfirmChecks));
        if (_navReady && ServerTypeIsVanilla)
            RebuildVersionList(keepSelection: true);
    }

    partial void OnPackConfirmedChanged(bool value)
    {
        if (value && ClientPackAcknowledged)
            RetainCurrentPack();
    }

    partial void OnClientPackAcknowledgedChanged(bool value)
    {
        if (value && PackConfirmed)
            RetainCurrentPack();
    }

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
            case nameof(ShowDeployElapsed):
            case nameof(DeployElapsedDisplay):
            case nameof(ShowDeployRemaining):
            case nameof(DeployRemainingDisplay):
            case nameof(CanCloseWizard):
            case nameof(CanMutateWizard):
            case nameof(DeployToolTip):
            case nameof(DeployProgressPercentDisplay):
            case nameof(ProfileDetailsText):
            case nameof(CreateResourcesConfirmText):
            case nameof(Vm1ShapeIsDefault):
            case nameof(Vm1ShapeIsSmaller):
            case nameof(VanillaFlavorIsDefault):
            case nameof(VanillaFlavorIsOptimized):
            case nameof(ShowSnapshotToggle):
            case nameof(ServerTypeIsVanilla):
            case nameof(ServerTypeIsModded):
            case nameof(ShowVanillaGameOptions):
            case nameof(ShowModdedGameOptions):
            case nameof(PackFileNameDisplay):
            case nameof(ShowPackSummary):
            case nameof(ShowPackConfirmChecks):
            case nameof(ClientPackCopy):
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
        OnPropertyChanged(nameof(ShowDeployRemaining));
        OnPropertyChanged(nameof(DeployRemainingDisplay));
        OnPropertyChanged(nameof(CanCloseWizard));
        OnPropertyChanged(nameof(CanMutateWizard));
        OnPropertyChanged(nameof(DeployToolTip));
        OnPropertyChanged(nameof(DeployProgressPercentDisplay));
        OnPropertyChanged(nameof(ProfileDetailsText));
        OnPropertyChanged(nameof(CreateResourcesConfirmText));
        OnPropertyChanged(nameof(Vm1ShapeIsDefault));
        OnPropertyChanged(nameof(Vm1ShapeIsSmaller));
        OnPropertyChanged(nameof(VanillaFlavorIsDefault));
        OnPropertyChanged(nameof(VanillaFlavorIsOptimized));
        OnPropertyChanged(nameof(ShowSnapshotToggle));
        OnPropertyChanged(nameof(ServerTypeIsVanilla));
        OnPropertyChanged(nameof(ServerTypeIsModded));
        OnPropertyChanged(nameof(ShowVanillaGameOptions));
        OnPropertyChanged(nameof(ShowModdedGameOptions));
        OnPropertyChanged(nameof(PackFileNameDisplay));
        OnPropertyChanged(nameof(ShowPackSummary));
        OnPropertyChanged(nameof(ShowPackConfirmChecks));
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
