using System.Windows;
using McManager.Hybrid.Ui;
using McManager.Hybrid.Ui.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class MainWindow : Window
{
    /// <summary>
    /// CSS <c>--app-shell-width</c>: chrome row (1008) + 16px padding each side.
    /// WindowStyle=None: this is the client width (no native caption frame).
    /// </summary>
    public const double AppShellWidthDip = 1040;

    public MainWindow()
    {
        InitializeComponent();
        FitWidthToShell();
        Loaded += (_, _) => FitWidthToWebView();
        Resources.Add("services", App.Services);

        App.Services.GetRequiredService<WpfWindowChromeService>().Attach(this);

        var focus = App.Services.GetRequiredService<WindowFocusBroker>();
        Activated += (_, _) => focus.SetFocused(WindowState != WindowState.Minimized);
        Deactivated += (_, _) => focus.SetFocused(false);
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                focus.SetFocused(false);
            else if (IsActive)
                focus.SetFocused(true);
        };
    }

    private void FitWidthToShell()
    {
        MinWidth = AppShellWidthDip;
        Width = AppShellWidthDip;
    }

    /// <summary>
    /// Non-client thickness can undershoot the real WebView2 client on Windows,
    /// which clips the right gutter and makes min-width padding look uneven.
    /// </summary>
    private void FitWidthToWebView()
    {
        if (HostView.ActualWidth <= 0)
            return;

        var nonClient = ActualWidth - HostView.ActualWidth;
        var outer = AppShellWidthDip + Math.Max(0, nonClient);
        if (outer > MinWidth)
            MinWidth = outer;
        if (Width + 0.5 < outer)
            Width = outer;
    }
}
