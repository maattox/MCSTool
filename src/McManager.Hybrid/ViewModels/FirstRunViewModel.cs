using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "";

    public FirstRunViewModel(
        ConnectExistingFlow flow,
        HybridShell shell,
        LocalConfigHost configHost)
    {
        _flow = flow;
        _shell = shell;
        _configHost = configHost;
    }

    public LocalConfigHost ConfigHost => _configHost;

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
                _configHost.Reload();
                _shell.EnterManage();
                return;
            }

            if (outcome == ConnectExistingOutcome.NoneFound)
                StatusMessage = "No product stack found. Nothing was written.";
            else if (outcome == ConnectExistingOutcome.Cancelled)
                StatusMessage = "Auto-detect cancelled. Nothing was written.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
