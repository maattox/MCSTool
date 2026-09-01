using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// First-run chooser: Setup wizard, button-gated Auto-detect, or skip to manage.
/// Does not scan OCI until the operator clicks Find an existing stack.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ConnectExistingFlow _flow;
    private readonly HybridShell _shell;
    private readonly LocalConfigHost _configHost;
    private readonly ManageSession _session;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    public FirstRunViewModel(
        ConnectExistingFlow flow,
        HybridShell shell,
        LocalConfigHost configHost,
        ManageSession session)
    {
        _flow = flow;
        _shell = shell;
        _configHost = configHost;
        _session = session;
    }

    public LocalConfigHost ConfigHost => _configHost;

    public bool CanCancelAdd => ServerCatalog.CanDiscardCurrentEmptyServer();

    public void CancelAdd()
    {
        if (IsBusy || !CanCancelAdd)
            return;

        var discarded = ServerCatalog.DiscardCurrentEmptyServer();
        if (!discarded.Succeeded)
        {
            StatusMessage = discarded.Error ?? "Could not cancel adding this server.";
            OnPropertyChanged(nameof(CanCancelAdd));
            return;
        }

        _session.ReloadFromDisk();
        OnPropertyChanged(nameof(CanCancelAdd));
        if (_configHost.HasManageConfig)
            _shell.EnterManage();
        else
            _shell.EnterFirstRun();
    }

    public void OpenSetup()
    {
        if (IsBusy)
            return;
        _shell.OpenSetup();
    }

    public void OpenExistingStack()
    {
        if (IsBusy)
            return;
        _session.ReloadFromDisk();
        _shell.EnterManage();
    }

    public async Task DetectAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Scanning…";
        var progress = new Progress<string>(msg => StatusMessage = msg);
        try
        {
            var outcome = await _flow.RunAsync(progress);
            if (outcome == ConnectExistingOutcome.Connected)
            {
                _session.ReloadFromDisk();
                _shell.EnterManage();
                return;
            }

            if (outcome == ConnectExistingOutcome.NoneFound)
                StatusMessage = "No product stack found. Nothing was written.";
            else if (outcome == ConnectExistingOutcome.Incompatible)
                StatusMessage = "Stack is incompatible with this Manager. Nothing was written.";
            else if (outcome == ConnectExistingOutcome.Cancelled)
                StatusMessage = "Auto-detect cancelled. Nothing was written.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
