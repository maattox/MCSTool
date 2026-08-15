using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.App.Dialogs;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;

namespace McManager.App.ViewModels;

public partial class SetupWizardViewModel : ViewModelBase
{
    public const string AlwaysFreeDocsUrl =
        "https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm#compute";

    public const string MinecraftEulaUrl = "https://aka.ms/MinecraftEULA";

    private readonly MojangVersionCatalog _catalog = new();
    private MojangVersionManifest? _manifest;
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();
    private DispatcherTimer? _logFlushTimer;

    public Window? Host { get; set; }

    public ObservableCollection<OciConfigProfile> Profiles { get; } = [];

    public ObservableCollection<string> VersionIds { get; } = [];

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
    private OciConfigProfile? _selectedProfile;

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

    private DispatcherTimer? _capacityTimer;
    private string _functionImage = "";
    private string _resumeMinecraftVersion = "";
    private bool _suppressVersionWriteback;
    private bool _navReady;

    public bool HasExistingManageConfig { get; }

    public bool IsTofuDryRun { get; } = ProductPaths.IsTofuDryRun();

    public string AuthTokenStoredDisplay =>
        AuthTokenStored
            ? "Stored in Credential Manager: yes (McManager/ocir)"
            : "Stored in Credential Manager: no";

    public SetupWizardViewModel()
    {
        LoadFrom(SetupWizardStore.LoadOrNew());
        LoadProfiles();
        AuthTokenStored = AuthTokenStored || WindowsCredentialStore.Exists();
        HasExistingManageConfig = LocalConfigStore.HasManageConfig();
        _navReady = true;
    }

    public bool CanGoBack => CurrentStep > 0 && !IsBusy;

    public bool IsLastStep => CurrentStep >= SetupWizardState.StepCount - 1;

    public bool CanGoNext =>
        CurrentStep < SetupWizardState.StepCount - 1 && StepIsValid(CurrentStep);

    public bool ShowDeployButton => IsLastStep && !CapacityWaiting;

    public bool ShowCapacityOptionsButton =>
        IsLastStep && CapacityWaiting && !IsPollingCapacity && !IsBusy;

    public bool CanDeploy =>
        ShowDeployButton
        && EulaAccepted
        && TfvarsWriter.NormalizeAdminCidr(AdminCidr) is not null
        && MinecraftUsername.IsMissingOrValid(AdminMinecraftUsername)
        && !IsBusy
        && CreateResourcesConfirmed
        && (!ShowReplaceConfigConfirm || ReplaceConfigConfirmed);

    public bool CanRetryDeploy =>
        CapacityWaiting
        && !IsBusy
        && EulaAccepted
        && TfvarsWriter.NormalizeAdminCidr(AdminCidr) is not null
        && MinecraftUsername.IsMissingOrValid(AdminMinecraftUsername);

    public bool ShowReplaceConfigConfirm => HasExistingManageConfig && !IsTofuDryRun;

    public string ProfileDetailsText =>
        SelectedProfile?.DetailsText
        ?? "Select a profile to confirm region, tenancy, and user from ~/.oci/config.";

    public string CreateResourcesConfirmText =>
        IsTofuDryRun
            ? "I understand this is a dry-run (no OCI resources and config.local.json will not be written)."
            : "Create Always Free VM1 (2 OCPU / 12 GB temporary test shape) + door Micro + reserved IP in the selected tenancy.";

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
        1 => "OCI profile",
        2 => "Compartment",
        3 => "Budget alert email",
        4 => "SSH key",
        5 => "Minecraft (Vanilla)",
        6 => "Mojang EULA",
        7 => "OCIR Auth Token",
        8 => "Plan summary",
        _ => "Setup",
    };

    public string StepSubtitle => $"Step {CurrentStep + 1} of {SetupWizardState.StepCount}";

    public string PlanSummaryText => InfraPlanSummary.Build(ToState());

    public async Task InitializeAsync()
    {
        await LoadVersionsAsync();
        await DetectAdminIpAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep <= 0)
            return;
        CurrentStep--;
        Persist();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (!CanGoNext)
            return;

        if (CurrentStep == 7)
            TryStoreAuthToken();

        CurrentStep++;
        Persist();
        await Task.CompletedTask;
    }

    [RelayCommand]
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
        Persist();
    }

    [RelayCommand]
    private void OpenAlwaysFreeDocs() => OpenUrl(AlwaysFreeDocsUrl);

    [RelayCommand]
    private void OpenEula() => OpenUrl(MinecraftEulaUrl);

    [RelayCommand]
    private async Task GenerateSshKeyAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Generating ed25519 key…";
        try
        {
            var result = await SshKeyHelper.GenerateEd25519Async();
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "SSH generate failed.";
                return;
            }

            SshGenerateMode = true;
            ApplySsh(result.Value, alreadyExisted: false);
            StatusMessage = $"Created {result.Value.Path} (private key stays on disk).";
            Persist();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportSshKeyAsync()
    {
        if (Host is null)
        {
            StatusMessage = "Window not ready for file picker.";
            return;
        }

        var storage = Host.StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import OpenSSH public key",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Public keys") { Patterns = ["*.pub"] },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Could not resolve the selected file path.";
            return;
        }

        var imported = SshKeyHelper.ImportPublicKey(path);
        if (!imported.Succeeded || imported.Value is null)
        {
            StatusMessage = imported.Error ?? "Import failed.";
            return;
        }

        SshGenerateMode = false;
        ApplySsh(imported.Value, alreadyExisted: true);
        StatusMessage = $"Imported {imported.Value.Path}";
        Persist();
    }

    [RelayCommand]
    private void StoreAuthToken()
    {
        TryStoreAuthToken();
        Persist();
    }

    [RelayCommand]
    private void ClearAuthToken()
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
    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private async Task DeployAsync()
    {
        if (!CanDeploy)
            return;
        await RunDeployPipelineAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRetryDeploy))]
    private async Task RetryDeployAsync()
    {
        if (!CanRetryDeploy)
            return;
        await RunDeployPipelineAsync();
    }

    [RelayCommand]
    private void StartCapacityPoll()
    {
        if (IsPollingCapacity)
            return;
        IsPollingCapacity = true;
        StatusMessage = "Auto-retrying every 5 minutes.";
        QueueLog("Auto-retry armed: apply every 5 minutes (silent on capacity failures).");
        _capacityTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _capacityTimer.Tick += async (_, _) =>
        {
            if (IsBusy || !IsPollingCapacity)
                return;
            await RunDeployPipelineAsync();
        };
        _capacityTimer.Start();
    }

    [RelayCommand]
    private void StopCapacityPoll()
    {
        _capacityTimer?.Stop();
        _capacityTimer = null;
        if (IsPollingCapacity)
            StatusMessage = "Auto-retry paused. Use Retry options to try again or resume.";
        IsPollingCapacity = false;
    }

    [RelayCommand]
    private async Task ShowCapacityOptionsAsync()
    {
        if (IsBusy)
            return;
        await PromptCapacityWaitAsync();
    }

    [RelayCommand]
    private async Task CopyPlanSummaryAsync() =>
        await CopyToClipboardAsync(PlanSummaryText, "Copied plan summary.");

    [RelayCommand]
    private async Task CopyDeployLogAsync()
    {
        FlushLog();
        await CopyToClipboardAsync(
            string.IsNullOrWhiteSpace(DeployLog) ? "(empty)" : DeployLog,
            "Copied deploy log.");
    }

    private async Task CopyToClipboardAsync(string text, string okMessage)
    {
        var clip = Host?.Clipboard;
        if (clip is null)
        {
            StatusMessage = "Clipboard unavailable.";
            return;
        }

        await clip.SetTextAsync(text);
        StatusMessage = okMessage;
    }

    private async Task RunDeployPipelineAsync()
    {
        var promptCapacity = false;
        IsBusy = true;
        StatusMessage = IsTofuDryRun ? "Dry-run deploy (no OCI)…" : "Deploying…";
        StartLogFlushTimer();
        try
        {
            var log = new BufferedProgress(QueueLog);
            var orch = new SetupDeployOrchestrator();
            var state = ToState();
            var result = await Task.Run(async () => await orch.RunAsync(state, log).ConfigureAwait(false))
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

                return;
            }

            if (result.Succeeded)
                CapacityWaiting = false;

            if (IsPollingCapacity && result.Succeeded)
                StopCapacityPoll();

            if (!result.Succeeded && !result.CapacityWait)
                CapacityWaiting = false;

            StatusMessage = ShortStatus(result.Message);
            Persist();
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
            await PromptCapacityWaitAsync();
    }

    private async Task PromptCapacityWaitAsync()
    {
        var choice = await CapacityWaitDialog.ShowAsync(Host);
        if (choice == CapacityWaitChoice.RetryNow)
            await RunDeployPipelineAsync();
        else if (choice == CapacityWaitChoice.AutoRetry)
            StartCapacityPoll();
    }

    private void StartLogFlushTimer()
    {
        if (_logFlushTimer is not null)
            return;
        _logFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _logFlushTimer.Tick += (_, _) => FlushLog();
        _logFlushTimer.Start();
    }

    private void StopLogFlushTimer()
    {
        FlushLog();
        _logFlushTimer?.Stop();
        _logFlushTimer = null;
    }

    private void QueueLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (_logLock)
            _logBuffer.AppendLine(line);
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
        var detected = await PublicIpDetector.FetchPublicIpAsync();
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
            var result = await _catalog.LoadAsync();
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
        VersionIds.Clear();
        foreach (var v in filtered)
            VersionIds.Add(v.Id);

        var target = !string.IsNullOrWhiteSpace(previous) && VersionIds.Contains(previous)
            ? previous
            : MojangVersionCatalog.DefaultVersionId(_manifest);
        ApplyMinecraftVersion(target);
        Persist();
    }

    private void ApplyMinecraftVersion(string? target)
    {
        target ??= "";
        if (!string.IsNullOrWhiteSpace(target))
            _resumeMinecraftVersion = target;

        void Assign()
        {
            _suppressVersionWriteback = true;
            try
            {
                MinecraftVersion = "";
                MinecraftVersion = target;
            }
            finally
            {
                _suppressVersionWriteback = false;
            }
        }

        Assign();
        Dispatcher.UIThread.Post(Assign, DispatcherPriority.Loaded);
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in OciConfigProfiles.List())
            Profiles.Add(p);

        SelectedProfile = Profiles.FirstOrDefault(p =>
                string.Equals(p.Name, OciProfile, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.FirstOrDefault();

        if (SelectedProfile is not null)
            OciRegion = SelectedProfile.Region;
    }

    private void ApplySsh(SshPublicKeyInfo info, bool alreadyExisted)
    {
        _ = alreadyExisted;
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

    partial void OnSelectedProfileChanged(OciConfigProfile? value)
    {
        if (value is null)
            return;
        OciProfile = value.Name;
        OciRegion = value.Region;
        OnPropertyChanged(nameof(ProfileDetailsText));
    }

    partial void OnCreateCompartmentChanged(bool value) => OnPropertyChanged(nameof(UseExistingCompartment));

    partial void OnSshGenerateModeChanged(bool value) => OnPropertyChanged(nameof(SshImportMode));

    partial void OnAuthTokenStoredChanged(bool value) => OnPropertyChanged(nameof(AuthTokenStoredDisplay));

    partial void OnMinecraftVersionChanged(string value)
    {
        if (_suppressVersionWriteback || string.IsNullOrWhiteSpace(value))
            return;
        _resumeMinecraftVersion = value;
    }

    partial void OnCurrentStepChanged(int value)
    {
        if (value == 5 && !string.IsNullOrWhiteSpace(_resumeMinecraftVersion))
            Dispatcher.UIThread.Post(
                () => ApplyMinecraftVersion(_resumeMinecraftVersion),
                DispatcherPriority.Loaded);
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
            case nameof(ProfileDetailsText):
            case nameof(CreateResourcesConfirmText):
                return;
        }

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
        OnPropertyChanged(nameof(ProfileDetailsText));
        if (!_navReady)
            return;

        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        DeployCommand.NotifyCanExecuteChanged();
        RetryDeployCommand.NotifyCanExecuteChanged();
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
