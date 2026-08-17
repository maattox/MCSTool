using System.ComponentModel;
using McManager.Core.Config;

namespace McManager.Hybrid;

/// <summary>
/// Host-level page switch for first-run, manage, and Setup. Does not run OpenTofu.
/// Does not probe OCI.
/// </summary>
public sealed class HybridShell : INotifyPropertyChanged
{
    public enum PageKind
    {
        FirstRun,
        Manage,
        Setup,
    }

    private PageKind _page;
    private PageKind _pageBeforeSetup = PageKind.Manage;

    public HybridShell(PageKind initialPage = PageKind.Manage)
    {
        _page = initialPage;
    }

    public PageKind Page
    {
        get => _page;
        private set
        {
            if (_page == value)
                return;
            _page = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Page)));
        }
    }

    /// <summary>Page to restore when leaving Setup if manage config is still missing.</summary>
    public PageKind PageBeforeSetup => _pageBeforeSetup;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void EnterManage() => Page = PageKind.Manage;

    public void EnterFirstRun() => Page = PageKind.FirstRun;

    public void OpenSetup()
    {
        // Does not tofu apply — Deploy inside the wizard is a separate click
        // (agents: MCMANAGER_TOFU_DRY_RUN=1).
        if (Page != PageKind.Setup)
            _pageBeforeSetup = Page;
        Page = PageKind.Setup;
    }

    /// <summary>
    /// Leave Setup. If <c>config.local.json</c> is now present, enter manage;
    /// otherwise restore the page that opened the wizard (usually first-run).
    /// </summary>
    public void CloseSetup() =>
        Page = LocalConfigStore.HasManageConfig() ? PageKind.Manage : _pageBeforeSetup;
}
