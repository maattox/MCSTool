using System.Windows;
using McManager.Hybrid.Ui;
using McManager.Hybrid.Ui.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class MainWindow : Window
{
    /// <summary>
    /// Default WebView client width (~1280 CSS px). WindowStyle=None: XAML Width is
    /// the outer window; 6px WPF strips sit outside the WebView.
    /// </summary>
    public const double AppShellWidthDip = 1280;

    /// <summary>
    /// Smallest WebView client that still fits the Manage sidebar plus a usable
    /// content pane. Must stay below <see cref="AppShellWidthDip"/> so resize is real.
    /// </summary>
    public const double AppShellMinWidthDip = 920;

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
        MinWidth = AppShellMinWidthDip;
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

        var nonClient = Math.Max(0, ActualWidth - HostView.ActualWidth);
        var minOuter = AppShellMinWidthDip + nonClient;
        var defaultOuter = AppShellWidthDip + nonClient;
        MinWidth = minOuter;
        if (Width + 0.5 < defaultOuter)
            Width = defaultOuter;
    }
}
