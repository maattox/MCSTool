using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;
using McManager.Hybrid;

namespace McManager.Hybrid.ViewModels;

public enum CapacityWaitChoice
{
    Dismissed,
    RetryNow,
    AutoRetry,
}

/// <summary>
/// Eight-step Setup wizard (Always Free → OCI+email → SSH →
/// game (Vanilla Default/Paper or Modded pack file) → name/icon → EULA → Auth Token → summary).
/// Compartment is auto-named <c>mcmgr</c> / <c>mcmgr-2</c> at Deploy. No Window Host —
/// pickers/clipboard/dialogs/clock via B3 interfaces. Does not tofu apply unless the
/// operator clicks Deploy; agents use <c>MCMANAGER_TOFU_DRY_RUN=1</c>.
/// </summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    public const string AlwaysFreeDocsUrl =
        "https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute";

    public const string MinecraftEulaUrl = "https://aka.ms/MinecraftEULA";

    public const string CapacityWaitExplanation =
        "Always Free A1 Flex host capacity is unavailable in this region right now. The game server was not created.\n\n"
        + "Other Always Free resources from this Setup (compartment, network, doorbell, reserved IP) may already exist. Retry reuses them; it does not start from scratch.\n\n"
        + "Try again now, or auto-retry every 5 minutes while Setup stays open. Auto-retry checks capacity first and stays silent on later failures.\n\n"
        + "Close returns to Setup so you can pause or resume later.";

    public const string DeployDurationHint =
        "Often 10–25 minutes. Leave this window open until it finishes.";

    public const long PackDropMaxBytes = 512L * 1024 * 1024;

    public const string AlwaysFreeStayHelp =
        "This product uses Always Free Ampere A1 for the game server plus a tiny doorbell computer. There is no paid mode. A1 capacity can be unavailable in the region.";

    public const string AlwaysFreeResidualHelp =
        "A $1 monthly budget is a last-resort brake that stops the game server. Oracle may still bill a small residual (~$1–$2) after that brake fires. This is not a hard $0 guarantee.";

    public const string AlwaysFreeCapacityHelp =
        "If A1 Flex is out of capacity, a window offers try again now, auto-retry every 5 minutes, or resume later. It does not spam the Oracle API.";

    public const string AlwaysFreeBodyCopy =
        "This product uses Always Free–eligible shapes: Ampere A1 for the game server and a tiny AMD Micro doorbell. The target is $0. A $1 monthly budget is the last-resort spend brake; Oracle may still bill about $1–$2 that month if it fires.";

    public const string OciProfileHelp =
        "Region and account details come from ~/.oci/config on this PC. Prefer the tenancy home region so Always Free A1 and Micro eligibility apply.";

    public const string AlertEmailHelp =
        "Oracle emails the $1 last-resort budget alert here. Use a comma between addresses if more than one.";

    public const string SshKeyHelp =
        "Create a new key on this PC, or import an existing public key. The private key stays on disk and is not saved in Setup’s resume file. This is not the Oracle API key.";

    public const string VanillaHelp =
        "Official Mojang jar, or Optimized Vanilla (Paper) for better multiplayer. Friends join with the same Java version. Paper is not Forge or Fabric.";

    public const string ModdedHelp =
        "Choose a local .mrpack or server-pack zip you already exported. There is no pack search. Friends need that same pack to join.";

    public const string DefaultVanillaHelp =
        "Official Mojang server jar. Same path as before.";

    public const string OptimizedVanillaHelp =
        "Better multiplayer performance. Not Forge or Fabric mods. Paper is a faster vanilla-compatible server.";

    public const string PackFileHelp = SetupPackImport.PackFileNoviceHelp;

    public const string ClientPackHelp = SetupPackImport.ClientPackCopy;

    public const string EulaHelp =
        "The installer writes eula.txt only if this is checked. This product will not auto-accept the EULA for you.";

    public const string AuthTokenHelp =
        "Needed later to push the spend-brake Function image. Stored in Windows Credential Manager, not in the Setup resume file. Skip and finish Setup if you do not have one yet.";

    public const string AdminCidrHelp =
        "Oracle’s cloud firewall allowlist. Friends you add later also need their public IPv4 as /32.";

    public const string ShapeDefaultHelp =
        "More room for players and later mods. Uses Always Free hours faster while the server is on. Chosen once at deploy — not a later resize.";

    public const string ShapeSmallerHelp =
        "Smaller Always Free size. Vanilla can often stay on all month; less room if you add mods or more players later.";

    public const string IdentityHelp =
        "Friends see the name, description, and in-game icon in Minecraft’s server list while the game is running. Each box is one list line (59 characters). Select text and apply colors, or paste a motd= string from a generator. Hex colors need Paper/Spigot 1.16+. You can change this later on the Server tab.";

    public const string IconStatesHelp =
        "In-game is the color icon while Minecraft is up. Offline, Starting, and Unavailable are greyscale copies with overlays for the doorbell list while the server is off, waking, or cannot start (daily hours or spend-brake).";

    private static readonly TimeSpan LogFlushPeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ElapsedTickPeriod = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CapacityPollPeriod = TimeSpan.FromMinutes(5);

    private readonly IFilePicker _picker;
    private readonly IClipboard _clipboard;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly PackIdentityCatalogCache _catalogs;
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
    private bool _applyingIdentityDefault;

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

    private SetupPackPreview? _packPreview;
    private HashSet<string> _operatorSkipTerms = new(StringComparer.OrdinalIgnoreCase);
    private bool _packLooksLikeLauncherInstance;

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
    private string _packJavaMajorText = "";

    [ObservableProperty]
    private bool _packNeedsIdentityConfirm;

    [ObservableProperty]
    private string _packSourcePath = "";

    [ObservableProperty]
    private string _detectedMinecraftVersion = "";

    [ObservableProperty]
    private string _detectedLoader = "";

    private bool _javaMajorCustomized;
    private bool _applyingJavaFloor;

    [ObservableProperty]
    private string _packSummary = "";

    [ObservableProperty]
    private string _packOverrideListWarning = "";

    [ObservableProperty]
    private string _packBlockReason = "";

    [ObservableProperty]
    private bool _packCanContinue;

    [ObservableProperty]
    private bool _packConfirmed;

    [ObservableProperty]
    private bool _clientPackAcknowledged;

    [ObservableProperty]
    private string _identityName = "";

    [ObservableProperty]
    private string _identityDescription = "";

    [ObservableProperty]
    private bool _identityNameCustomized;

    [ObservableProperty]
    private bool _identityDescriptionCustomized;

    [ObservableProperty]
    private string _identityIconPath = "";

    [ObservableProperty]
    private string _iconPreviewDataUrl = "";

    [ObservableProperty]
    private string _idleIconPreviewDataUrl = "";

    [ObservableProperty]
    private string _startingIconPreviewDataUrl = "";

    [ObservableProperty]
    private string _exhaustedIconPreviewDataUrl = "";

    [ObservableProperty]
    private string _identityHint = "";

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

    [ObservableProperty]
    private string _reservedPlayIp = "";

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
        IUiDispatcher dispatcher,
        PackIdentityCatalogCache catalogs)
    {
        _picker = picker;
        _clipboard = clipboard;
        _clock = clock;
        _dispatcher = dispatcher;
        _catalogs = catalogs;
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

    public bool ShowDeploySuccess =>
        IsLastStep
        && !IsBusy
        && !CapacityWaiting
        && string.Equals(ApplyStage, SetupApplyStage.ConfigWritten, StringComparison.Ordinal);

    public bool HasReservedPlayIp => !string.IsNullOrWhiteSpace(ReservedPlayIp);

    public bool ShowDeployButton => IsLastStep && !CapacityWaiting && !ShowDeploySuccess;

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

    public string DeployElapsedDisplay => ProgressDockUx.FormatElapsed(CurrentDeployElapsed());

    public string DockStatus =>
        ProgressDockUx.OneLineStatus(ShowDeployProgress, DeployProgressCaption, StatusMessage);

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
            : $"Create the Always Free game server ({Vm1ShapeChoice.Format(Vm1Ocpus, Vm1MemoryGb)}), doorbell, and reserved play IP in this Oracle account.";

    public string AutoRetryBannerText =>
        "Auto-retrying every 5 minutes until A1 capacity is available. Failures stay silent. Use Pause auto-retry to stop.";

    public bool IsStepAlwaysFree => CurrentStep == SetupWizardState.StepAlwaysFree;
    public bool IsStepOci => CurrentStep == SetupWizardState.StepOci;
    public bool IsStepSsh => CurrentStep == SetupWizardState.StepSsh;
    public bool IsStepGame => CurrentStep == SetupWizardState.StepGame;
    public bool IsStepIdentity => CurrentStep == SetupWizardState.StepIdentity;
    public bool IsStepEula => CurrentStep == SetupWizardState.StepEula;
    public bool IsStepAuthToken => CurrentStep == SetupWizardState.StepAuthToken;
    public bool IsStepSummary => CurrentStep == SetupWizardState.StepSummary;

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

    public bool ShowPackIdentityFields =>
        ShowPackSummary && PackCanContinue && PackNeedsIdentityConfirm;

    public bool PackIdentityComplete =>
        !PackNeedsIdentityConfirm
        || DerivedPackIdentity.IsComplete(
            MinecraftVersion,
            PackLoader,
            PackLoaderVersion,
            PackJavaMajorText);

    public bool ShowDetectionMismatch =>
        ShowPackIdentityFields
        && DerivedPackIdentity.DisagreesWithDetection(
            DetectedMinecraftVersion,
            DetectedLoader,
            MinecraftVersion,
            PackLoader);

    public string DetectionMismatchWarning =>
        DerivedPackIdentity.FormatDetectionMismatchWarning(
            DetectedMinecraftVersion,
            DetectedLoader,
            MinecraftVersion,
            PackLoader);

    public bool ShowOverrideListWarning =>
        ShowPackSummary && PackCanContinue && !string.IsNullOrWhiteSpace(PackOverrideListWarning);

    public bool ShowPackAssistedReview =>
        ShowPackSummary
        && PackCanContinue
        && _packPreview is not null
        && (_packPreview.NeedsAssistedReview
            || !PackReplaceUx.FreezeAllowsContinue(_packPreview.FreezeBlockReason)
            || _packPreview.AssistedReview.WillSkip.Any(i => i.SkipReason == PackFileSkipReason.OperatorSkip)
            || (_operatorSkipTerms.Count > 0 && _packPreview.Kind == SetupPackImport.KindManualZip));

    public PackAssistedReview AssistedReview =>
        _packPreview?.AssistedReview ?? PackAssistedReview.Empty;

    public IReadOnlyList<string> PackJarOrder =>
        _packPreview?.JarRecords.Select(j => j.Path).ToArray() ?? [];

    public string PackFreezeBlockReason => _packPreview?.FreezeBlockReason ?? "";

    public bool PackLooksLikeLauncherInstance => _packLooksLikeLauncherInstance;

    public string GameStepNextTitle =>
        PackReplaceUx.FreezeAllowsContinue(PackFreezeBlockReason)
            ? ""
            : PackFreezeBlockReason;

    public bool IsOperatorSkipped(string path) =>
        PackAssistedReviewActions.IsSkipped(_operatorSkipTerms, path);

    public string ClientPackTitle => SetupPackImport.ClientPackTitle;

    public string ClientPackCopy => SetupPackImport.ClientPackCopy;

    public string ClientPackAckLabel => SetupPackImport.ClientPackAckLabel;

    public string ClientPackFriendsNeed =>
        SetupPackImport.FriendsNeedLine(PackName, MinecraftVersion, PackLoader, PackLoaderVersion);

    public string MotdPreview =>
        ServerIdentityUx.BuildMotd(IdentityName, IdentityDescription);

    public bool CanClearIdentityIcon =>
        !string.IsNullOrWhiteSpace(IdentityIconPath);

    public void SelectDefaultVanilla() => VanillaFlavor = SetupVanillaFlavor.Default;

    public void SelectOptimizedVanilla() => VanillaFlavor = SetupVanillaFlavor.Optimized;

    public void SelectVanillaServer() => ServerType = SetupServerType.Vanilla;

    public void SelectModdedServer() => ServerType = SetupServerType.Modded;

    public string StepTitle => CurrentStep switch
    {
        SetupWizardState.StepAlwaysFree => "Always Free",
        SetupWizardState.StepOci => "Oracle Cloud",
        SetupWizardState.StepSsh => "SSH key",
        SetupWizardState.StepGame => "Minecraft",
        SetupWizardState.StepIdentity => "Name and icon",
        SetupWizardState.StepEula => "Mojang EULA",
        SetupWizardState.StepAuthToken => "Optional Auth Token",
        SetupWizardState.StepSummary => ShowDeploySuccess ? "Deployment Complete" : "Review and deploy",
        _ => "Setup",
    };

    public string StepSubtitle =>
        ShowDeploySuccess
            ? "Close this wizard to continue to the Manager app."
            : $"Step {CurrentStep + 1} of {SetupWizardState.StepCount}";

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

        if (CurrentStep == SetupWizardState.StepAuthToken)
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

    public async Task PickIdentityIconAsync()
    {
        if (IsDeployLocked)
            return;

        var path = await _picker.OpenFileAsync(new FilePickRequest
        {
            Title = "Choose a PNG server icon",
            Filters =
            [
                new FileTypeFilter("PNG images", ".png"),
                new FileTypeFilter("All files", ".*"),
            ],
        }).ConfigureAwait(true);

        if (string.IsNullOrWhiteSpace(path))
            return;

        ApplyIdentityIconPath(path);
        Persist();
    }

    public void ClearIdentityIcon()
    {
        if (IsDeployLocked)
            return;
        IdentityIconPath = "";
        IdentityHint = "";
        ApplyDefaultIconPreviews();
        OnPropertyChanged(nameof(CanClearIdentityIcon));
        Persist();
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
        PackJavaMajorText = "";
        PackNeedsIdentityConfirm = false;
        PackSourcePath = "";
        DetectedMinecraftVersion = "";
        DetectedLoader = "";
        _javaMajorCustomized = false;
        PackSummary = "";
        PackOverrideListWarning = "";
        PackBlockReason = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        PackAnalyzeCaption = "";
        _packPreview = null;
        _operatorSkipTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _packLooksLikeLauncherInstance = false;
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
        PackOverrideListWarning = "";
        PackCanContinue = false;
        if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }

        StatusMessage = "Analyzing modpack…";
        try
        {
            var result = await Task.Run(() =>
                SetupPackImport.AnalyzeFile(path, ExcludeIncludeListRefresh.Shared)).ConfigureAwait(true);
            if (!result.Succeeded || result.Value is null)
            {
                PackPath = path;
                PackSummary = "";
                PackOverrideListWarning = "";
                PackCanContinue = false;
                PackConfirmed = false;
                PackBlockReason = result.Error ?? "Could not analyze this file.";
                StatusMessage = PackBlockReason;
                Persist();
                return;
            }

            ApplyPackPreview(result.Value, keepConfirm);
            StatusMessage = result.Value.CanContinue
                ? (result.Value.NeedsAssistedReview
                    ? "Review unknown jars, then confirm before continuing."
                    : "Review the pack summary, then confirm before continuing.")
                : (result.Value.BlockReason ?? "This pack cannot be installed.");
            Persist();
        }
        catch (Exception ex)
        {
            PackCanContinue = false;
            PackConfirmed = false;
            PackOverrideListWarning = "";
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

    public Task OnAssistedSkipChanged(PackAssistedReviewActions.OperatorSkipChange change) =>
        SetOperatorSkipAsync(change.Path, change.Skip);

    public async Task SetOperatorSkipAsync(string path, bool skip)
    {
        if (_packPreview is null || IsDeployLocked)
            return;

        var result = PackAssistedReviewActions.ApplySkip(_packPreview, _operatorSkipTerms, path, skip);
        if (result.NeedsReanalyze)
        {
            await AnalyzePackPathAsync(_packPreview.SourcePath, keepConfirm: true).ConfigureAwait(true);
            return;
        }

        ApplyReviewPreview(result.Preview);
        if (!PackReplaceUx.FreezeAllowsContinue(result.Preview.FreezeBlockReason))
            StatusMessage = result.Preview.FreezeBlockReason ?? "";
    }

    private void ApplyReviewPreview(SetupPackPreview preview)
    {
        _packPreview = preview;
        PackSummary = preview.ConfirmableSummary;
        PackOverrideListWarning = preview.OverrideListWarning ?? "";
        NotifyAssistedReviewUi();
    }

    private void ApplyPackPreview(SetupPackPreview preview, bool keepConfirm)
    {
        _operatorSkipTerms = PackAssistedReviewActions.LoadPersistedSkipTerms(preview.SourcePath);
        _packLooksLikeLauncherInstance = SetupPackImport.LooksLikeLauncherInstance(preview.SourcePath);
        var bound = _operatorSkipTerms.Count > 0
            ? preview.ApplyOperatorSkips(_operatorSkipTerms)
            : preview;
        _packPreview = bound;
        preview = bound;

        PackPath = preview.SourcePath;
        PackKind = preview.Kind;
        PackName = preview.PackName;
        PackVersionId = preview.VersionId ?? "";
        PackLoader = preview.Loader;
        PackLoaderVersion = preview.LoaderVersion;
        PackNeedsIdentityConfirm = preview.NeedsIdentityConfirm;
        DetectedMinecraftVersion = preview.DetectedMinecraftVersion;
        DetectedLoader = preview.DetectedLoader;
        if (!preview.IsDerived)
            PackSourcePath = preview.SourcePath;
        PackSummary = preview.ConfirmableSummary;
        PackOverrideListWarning = preview.OverrideListWarning ?? "";
        PackBlockReason = preview.BlockReason ?? "";
        PackCanContinue = preview.CanContinue;
        if (!preview.CanContinue)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
            NotifyPackIdentityUi();
            NotifyAssistedReviewUi();
            return;
        }

        var mc = string.Equals(preview.MinecraftVersion, "(unknown)", StringComparison.OrdinalIgnoreCase)
            ? ""
            : preview.MinecraftVersion;
        MinecraftVersion = mc;
        _resumeMinecraftVersion = mc;
        PackJavaMajorText = preview.JavaMajor?.ToString() ?? "";
        _javaMajorCustomized = false;
        if (keepConfirm && PackConfirmed && ClientPackAcknowledged)
            RetainCurrentPack();
        else if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }
        NotifyPackIdentityUi();
        NotifyAssistedReviewUi();
    }

    private void RetainCurrentPack()
    {
        if (string.IsNullOrWhiteSpace(PackPath) || !PackCanContinue)
            return;

        if (PackNeedsIdentityConfirm)
        {
            if (!PackIdentityComplete)
                return;
            var dataDir = LocalConfigStore.TryFindDataDirectory();
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                StatusMessage = "Could not find Manager data directory for the derived pack.";
                return;
            }

            var source = string.IsNullOrWhiteSpace(PackSourcePath) ? PackPath : PackSourcePath;
            if (!File.Exists(source))
            {
                StatusMessage = "Original pack file is missing. Choose the pack again.";
                return;
            }

            var build = DerivedPackWorkflow.BuildAndRetain(
                source,
                PackName,
                string.IsNullOrWhiteSpace(PackVersionId) ? null : PackVersionId,
                MinecraftVersion,
                PackLoader,
                PackLoaderVersion,
                PackJavaMajorText,
                dataDir,
                Path.GetFileName(source));
            if (!build.Succeeded || string.IsNullOrWhiteSpace(build.Value))
            {
                StatusMessage = build.Error ?? "Could not build the derived pack.";
                return;
            }

            PackPath = build.Value;
            StatusMessage = "Derived pack saved for install and Download pack.";
            Persist();
            return;
        }

        if (!File.Exists(PackPath))
            return;
        var retainDir = LocalConfigStore.TryFindDataDirectory();
        if (string.IsNullOrWhiteSpace(retainDir))
            return;
        var retained = ImportedPackArchiveStore.Retain(
            PackPath,
            PackName,
            string.IsNullOrWhiteSpace(PackVersionId) ? null : PackVersionId,
            PackLoader,
            MinecraftVersion,
            retainDir);
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

    public async Task CopyReservedPlayIpAsync()
    {
        if (!HasReservedPlayIp)
        {
            StatusMessage = "No play IP to copy.";
            return;
        }

        await CopyToClipboardAsync(ReservedPlayIp, "Copied play IP.").ConfigureAwait(true);
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
            CreateCompartment = true;
            ExistingCompartmentId = "";
            if (!string.IsNullOrWhiteSpace(state.CompartmentName))
                CompartmentName = state.CompartmentName;
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
                {
                    CapacityWaiting = false;
                    RefreshReservedPlayIp(result.Outputs?.PlayReservedPublicIp);
                }

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
        OnPropertyChanged(nameof(DockStatus));
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
        string? human = null;
        lock (_logLock)
        {
            foreach (var raw in line.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                _logBuffer.Append('[').Append(stamp).Append("] ").AppendLine(raw.TrimEnd());
                human = ProgressDockUx.TryHumanizeLogLine(raw) ?? human;
            }
        }

        if (human is null)
            return;

        void ApplyCaption()
        {
            DeployProgressCaption = human;
            OnPropertyChanged(nameof(DockStatus));
        }

        if (_dispatcher.CheckAccess())
            ApplyCaption();
        else
            _ = _dispatcher.InvokeAsync(ApplyCaption);
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
            var mojang = await _catalogs.GetMojangAsync().ConfigureAwait(true);
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
        CreateCompartment = true;
        ExistingCompartmentId = "";
        CompartmentName = string.IsNullOrWhiteSpace(state.CompartmentName)
            ? CompartmentNamer.BaseName
            : state.CompartmentName;
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
        PackJavaMajorText = state.PackJavaMajor?.ToString() ?? "";
        PackSourcePath = state.PackSourcePath ?? "";
        PackNeedsIdentityConfirm = false;
        _javaMajorCustomized = state.PackJavaMajor is not null;
        PackSummary = state.PackSummary;
        PackConfirmed = state.PackConfirmed;
        ClientPackAcknowledged = state.ClientPackAcknowledged;
        _applyingIdentityDefault = true;
        IdentityName = MotdFormatting.ClipToListLine(state.IdentityName);
        IdentityDescription = MotdFormatting.ClipToListLine(state.IdentityDescription);
        IdentityNameCustomized = state.IdentityNameCustomized;
        IdentityDescriptionCustomized = state.IdentityDescriptionCustomized;
        IdentityIconPath = state.IdentityIconPath ?? "";
        _applyingIdentityDefault = false;
        ApplyIdentityDefaultsIfUntouched();
        ApplyIdentityIconPath(IdentityIconPath, persistHint: false);
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
        if (string.Equals(ApplyStage, SetupApplyStage.ConfigWritten, StringComparison.Ordinal))
        {
            IsDeployLocked = true;
            CurrentStep = SetupWizardState.StepCount - 1;
        }
        RefreshReservedPlayIp();
    }

    public SetupWizardState ToState() => new()
    {
        SchemaVersion = SetupWizardState.CurrentSchemaVersion,
        CurrentStep = CurrentStep,
        AlwaysFreeConfirmed = AlwaysFreeConfirmed,
        ResidualChargeDisclosed = ResidualChargeDisclosed,
        CapacityWaitConsent = CapacityWaitConsent,
        OciProfile = OciProfile,
        OciRegion = OciRegion,
        CreateCompartment = true,
        CompartmentName = string.IsNullOrWhiteSpace(CompartmentName) ? CompartmentNamer.BaseName : CompartmentName,
        ExistingCompartmentId = "",
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
        PackJavaMajor = DerivedPackIdentity.TryNormalizeJavaMajor(PackJavaMajorText, out var j) ? j : null,
        PackSourcePath = PackSourcePath,
        PackSummary = PackSummary,
        PackConfirmed = PackConfirmed,
        ClientPackAcknowledged = ClientPackAcknowledged,
        IdentityName = MotdFormatting.ClipToListLine(IdentityName),
        IdentityDescription = MotdFormatting.ClipToListLine(IdentityDescription),
        IdentityNameCustomized = IdentityNameCustomized,
        IdentityDescriptionCustomized = IdentityDescriptionCustomized,
        IdentityMotdOmitName = false,
        IdentityIconPath = IdentityIconPath ?? "",
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
        SetupWizardState.StepAlwaysFree => AlwaysFreeConfirmed && ResidualChargeDisclosed && CapacityWaitConsent,
        SetupWizardState.StepOci => !string.IsNullOrWhiteSpace(OciProfile)
            && !string.IsNullOrWhiteSpace(OciRegion)
            && AlertEmail.Contains('@', StringComparison.Ordinal),
        SetupWizardState.StepSsh => SshKeyHelper.LooksLikePublicKey(SshPublicKey),
        SetupWizardState.StepGame => SetupServerType.IsModded(ServerType)
            ? PackConfirmed
                && ClientPackAcknowledged
                && PackCanContinue
                && PackIdentityComplete
                && PackReplaceUx.FreezeAllowsContinue(PackFreezeBlockReason)
                && !string.IsNullOrWhiteSpace(
                    string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion)
            : !string.IsNullOrWhiteSpace(
                string.IsNullOrWhiteSpace(MinecraftVersion) ? _resumeMinecraftVersion : MinecraftVersion),
        SetupWizardState.StepIdentity => !string.IsNullOrWhiteSpace(IdentityName),
        SetupWizardState.StepEula => EulaAccepted,
        SetupWizardState.StepAuthToken => true,
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

    partial void OnSshGenerateModeChanged(bool value) => OnPropertyChanged(nameof(SshImportMode));

    partial void OnAuthTokenStoredChanged(bool value) => OnPropertyChanged(nameof(AuthTokenStoredDisplay));

    partial void OnMinecraftVersionChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        _resumeMinecraftVersion = value;
        if (PackNeedsIdentityConfirm && !_javaMajorCustomized)
        {
            _applyingJavaFloor = true;
            PackJavaMajorText = DerivedPackIdentity.JavaMajorForMinecraftOrNull(value)?.ToString() ?? "";
            _applyingJavaFloor = false;
        }
        NotifyPackIdentityUi();
    }

    partial void OnPackLoaderChanged(string value) => NotifyPackIdentityUi();

    partial void OnPackLoaderVersionChanged(string value) => NotifyPackIdentityUi();

    partial void OnPackJavaMajorTextChanged(string value)
    {
        if (_navReady && !_applyingJavaFloor)
            _javaMajorCustomized = true;
        NotifyPackIdentityUi();
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
        if (_navReady)
            ApplyIdentityDefaultsIfUntouched();
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
        if (_navReady)
            ApplyIdentityDefaultsIfUntouched();
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

    private void NotifyPackIdentityUi()
    {
        OnPropertyChanged(nameof(PackIdentityComplete));
        OnPropertyChanged(nameof(ShowPackIdentityFields));
        OnPropertyChanged(nameof(ShowDetectionMismatch));
        OnPropertyChanged(nameof(DetectionMismatchWarning));
        OnPropertyChanged(nameof(ClientPackFriendsNeed));
        OnPropertyChanged(nameof(ShowPackConfirmChecks));
        OnPropertyChanged(nameof(ShowPackAssistedReview));
    }

    private void NotifyAssistedReviewUi()
    {
        OnPropertyChanged(nameof(ShowPackAssistedReview));
        OnPropertyChanged(nameof(AssistedReview));
        OnPropertyChanged(nameof(PackJarOrder));
        OnPropertyChanged(nameof(PackFreezeBlockReason));
        OnPropertyChanged(nameof(PackLooksLikeLauncherInstance));
        OnPropertyChanged(nameof(GameStepNextTitle));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnIdentityNameChanged(string value)
    {
        if (_navReady && !_applyingIdentityDefault)
            IdentityNameCustomized = true;
        OnPropertyChanged(nameof(MotdPreview));
    }

    partial void OnIdentityDescriptionChanged(string value)
    {
        if (_navReady && !_applyingIdentityDefault)
            IdentityDescriptionCustomized = true;
        OnPropertyChanged(nameof(MotdPreview));
    }

    private void ApplyIdentityDefaultsIfUntouched()
    {
        _applyingIdentityDefault = true;
        try
        {
            if (!IdentityNameCustomized)
                IdentityName = ServerIdentityUx.DefaultServerName(ServerType, VanillaFlavor);
            if (!IdentityDescriptionCustomized)
                IdentityDescription = ServerIdentityUx.DefaultDescription;
        }
        finally
        {
            _applyingIdentityDefault = false;
        }
    }

    private void ApplyIdentityIconPath(string? path, bool persistHint = true)
    {
        if (!persistHint)
            IdentityHint = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            IdentityIconPath = "";
            ApplyDefaultIconPreviews();
            if (persistHint)
                IdentityHint = "";
            OnPropertyChanged(nameof(CanClearIdentityIcon));
            return;
        }

        if (!File.Exists(path))
        {
            IdentityIconPath = path;
            ApplyDefaultIconPreviews();
            IdentityHint = "That icon file is missing. Choose it again, or continue with the default.";
            OnPropertyChanged(nameof(CanClearIdentityIcon));
            return;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            IdentityHint = "Could not read that icon file. " + ex.Message;
            OnPropertyChanged(nameof(CanClearIdentityIcon));
            return;
        }

        var composed = ServerIconComposer.Compose(bytes);
        if (!composed.Succeeded || composed.Value is null)
        {
            IdentityHint = composed.Error ?? "Could not use that PNG.";
            OnPropertyChanged(nameof(CanClearIdentityIcon));
            return;
        }

        IdentityIconPath = path;
        ApplyIconPreviews(composed.Value);
        IdentityHint = "";
        OnPropertyChanged(nameof(CanClearIdentityIcon));
    }

    private void ApplyDefaultIconPreviews()
    {
        var composed = ServerIconComposer.Compose();
        if (composed.Succeeded && composed.Value is not null)
            ApplyIconPreviews(composed.Value);
        else
            ApplyIconPreviews(null);
    }

    private void ApplyIconPreviews(ServerIconSet? set)
    {
        if (set is null)
        {
            IconPreviewDataUrl = "";
            IdleIconPreviewDataUrl = "";
            StartingIconPreviewDataUrl = "";
            ExhaustedIconPreviewDataUrl = "";
            return;
        }

        IconPreviewDataUrl = set.ColorDataUrl;
        IdleIconPreviewDataUrl = set.IdleDataUrl;
        StartingIconPreviewDataUrl = set.StartingDataUrl;
        ExhaustedIconPreviewDataUrl = set.ExhaustedDataUrl;
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
            case nameof(IsStepSsh):
            case nameof(IsStepGame):
            case nameof(IsStepIdentity):
            case nameof(IsStepEula):
            case nameof(IsStepAuthToken):
            case nameof(IsStepSummary):
            case nameof(SshImportMode):
            case nameof(AuthTokenStoredDisplay):
            case nameof(StatusMessage):
            case nameof(DeployProgressCaption):
                OnPropertyChanged(nameof(DockStatus));
                return;
            case nameof(DockStatus):
            case nameof(AuthTokenInput):
            case nameof(DeployLog):
            case nameof(CanDeploy):
            case nameof(CanRetryDeploy):
            case nameof(ShowDeployButton):
            case nameof(ShowDeploySuccess):
            case nameof(HasReservedPlayIp):
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
            case nameof(ShowOverrideListWarning):
            case nameof(ClientPackTitle):
            case nameof(ClientPackCopy):
            case nameof(ClientPackAckLabel):
            case nameof(ClientPackFriendsNeed):
            case nameof(MotdPreview):
            case nameof(CanClearIdentityIcon):
            case nameof(IdentityHint):
            case nameof(IconPreviewDataUrl):
            case nameof(IdleIconPreviewDataUrl):
            case nameof(StartingIconPreviewDataUrl):
            case nameof(ExhaustedIconPreviewDataUrl):
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
        OnPropertyChanged(nameof(IsStepSsh));
        OnPropertyChanged(nameof(IsStepGame));
        OnPropertyChanged(nameof(IsStepIdentity));
        OnPropertyChanged(nameof(IsStepEula));
        OnPropertyChanged(nameof(IsStepAuthToken));
        OnPropertyChanged(nameof(IsStepSummary));
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanRetryDeploy));
        OnPropertyChanged(nameof(ShowDeployButton));
        OnPropertyChanged(nameof(ShowDeploySuccess));
        OnPropertyChanged(nameof(HasReservedPlayIp));
        OnPropertyChanged(nameof(ShowCapacityOptionsButton));
        OnPropertyChanged(nameof(ShowReplaceConfigConfirm));
        OnPropertyChanged(nameof(ShowDeployProgress));
        OnPropertyChanged(nameof(ShowDeployRemaining));
        OnPropertyChanged(nameof(DeployRemainingDisplay));
        OnPropertyChanged(nameof(DockStatus));
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
        OnPropertyChanged(nameof(ShowOverrideListWarning));
        OnPropertyChanged(nameof(ClientPackFriendsNeed));
        OnPropertyChanged(nameof(MotdPreview));
        OnPropertyChanged(nameof(CanClearIdentityIcon));
        OnPropertyChanged(nameof(SshImportMode));
        OnPropertyChanged(nameof(AuthTokenStoredDisplay));
    }

    private void RefreshReservedPlayIp(string? preferred = null)
    {
        ReservedPlayIp = SanitizePlayIp(preferred)
            ?? TryReadPlayIpFromLocalConfig()
            ?? TryReadPlayIpFromTofuOutputs()
            ?? "";
    }

    private static string? TryReadPlayIpFromLocalConfig()
    {
        var loaded = LocalConfigStore.Load();
        return SanitizePlayIp(loaded.Config?.Play.ReservedPublicIp);
    }

    private string? TryReadPlayIpFromTofuOutputs()
    {
        var stackId = string.IsNullOrWhiteSpace(CompartmentName)
            ? TofuWorkspace.DefaultStackId
            : CompartmentName;
        var path = Path.Combine(
            TofuWorkspace.TofuRootDirectory(),
            TofuWorkspace.Sanitize(stackId),
            "outputs.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var parsed = TofuApplyOutputs.Parse(File.ReadAllText(path));
            return SanitizePlayIp(parsed.Value?.PlayReservedPublicIp);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? SanitizePlayIp(string? value)
    {
        var ip = (value ?? "").Trim();
        if (ip.Length == 0)
            return null;
        if (ip.StartsWith("ocid1.", StringComparison.OrdinalIgnoreCase))
            return null;
        var slash = ip.IndexOf('/');
        if (slash > 0)
            ip = ip[..slash].Trim();
        return System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
    }

    partial void OnReservedPlayIpChanged(string value) =>
        OnPropertyChanged(nameof(HasReservedPlayIp));

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
