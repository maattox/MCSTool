using System.Windows;
using McManager.Hybrid.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class MainWindow : Window
{
    /// <summary>
    /// CSS <c>--app-shell-width</c>: chrome row (754) + 16px padding each side.
    /// </summary>
    public const double AppShellWidthDip = 786;

    public MainWindow()
    {
        InitializeComponent();
        FitWidthToShell();
        Loaded += (_, _) => FitWidthToWebView();
        Resources.Add("services", App.Services);

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
        var frame = SystemParameters.WindowNonClientFrameThickness;
        var outer = AppShellWidthDip + frame.Left + frame.Right;
        MinWidth = outer;
        Width = outer;
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
        var outer = AppShellWidthDip + nonClient;
        if (outer > MinWidth)
            MinWidth = outer;
        if (Width + 0.5 < outer)
            Width = outer;
    }
}
