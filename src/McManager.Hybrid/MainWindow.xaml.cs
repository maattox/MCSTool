using System.Windows;
using McManager.Hybrid.Ui;
using McManager.Hybrid.Ui.Wpf;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class MainWindow : Window
{
    /// <summary>
    /// Default WebView client width (~1280 CSS px). WindowStyle=None: XAML Width is
    /// the outer window; <see cref="FitWidthToWebView"/> adds remaining non-client thickness.
    /// </summary>
    public const double AppShellWidthDip = 1280;

    /// <summary>
    /// Smallest WebView client that still fits the Manage sidebar plus a usable
    /// content pane. Must stay below <see cref="AppShellWidthDip"/> so resize is real.
    /// </summary>
    public const double AppShellMinWidthDip = 920;

    /// <summary>
    /// Default WebView client height. Must stay above <see cref="AppShellMinHeightDip"/>.
    /// </summary>
    public const double AppShellHeightDip = 752;

    /// <summary>
    /// Smallest WebView client that still fits status, power, the 23px pin
    /// floor, and the eight sidebar tabs at their 2px minimum gap.
    /// </summary>
    public const double AppShellMinHeightDip = 553;

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
        MinHeight = AppShellMinHeightDip;
        Height = AppShellHeightDip;
    }

    /// <summary>
    /// Non-client thickness can undershoot the real WebView2 client on Windows,
    /// which clips the right gutter and makes min-width padding look uneven.
    /// </summary>
    private void FitWidthToWebView()
    {
        if (HostView.ActualWidth <= 0)
            return;

        var nonClientW = Math.Max(0, ActualWidth - HostView.ActualWidth);
        MinWidth = AppShellMinWidthDip + nonClientW;
        var defaultOuterW = AppShellWidthDip + nonClientW;
        if (Width + 0.5 < defaultOuterW)
            Width = defaultOuterW;

        if (HostView.ActualHeight <= 0)
            return;

        var nonClientH = Math.Max(0, ActualHeight - HostView.ActualHeight);
        MinHeight = AppShellMinHeightDip + nonClientH;
        var defaultOuterH = AppShellHeightDip + nonClientH;
        if (Height + 0.5 < defaultOuterH)
            Height = defaultOuterH;
    }

    private void OnBlazorWebViewInitialized(object sender, BlazorWebViewInitializedEventArgs e)
    {
        var webView = e.WebView;
        webView.ZoomFactor = 1;
        var core = webView.CoreWebView2;
        if (core is null)
            return;

        core.Settings.IsZoomControlEnabled = false;
        // Autofill on first text-field focus can freeze WebView2 + custom WindowChrome for a beat.
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
    }
}
