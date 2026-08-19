using System.Windows;
using System.Windows.Threading;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Hybrid.Ui;
using McManager.Hybrid.Ui.Wpf;
using McManager.Hybrid.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!WebView2RuntimeGuard.TryEnsureRuntime(out var error))
        {
            WebView2RuntimeGuard.ShowMissingRuntimeMessage(error);
            Shutdown();
            return;
        }

        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();
        RegisterUiHostServices(services);
        RegisterManageServices(services);
        Services = services.BuildServiceProvider();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is IDisposable disposable)
            disposable.Dispose();

        base.OnExit(e);
    }

    private void RegisterUiHostServices(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(Dispatcher));
        services.AddSingleton<IUiClock, WpfUiClock>();
        services.AddSingleton<IClipboard, WpfClipboard>();
        services.AddSingleton<IShell, WpfShell>();
        services.AddSingleton<IFilePicker, WpfFilePicker>();
        services.AddSingleton<UiDialogs>();
        services.AddSingleton<IUiDialogs>(sp => sp.GetRequiredService<UiDialogs>());
        services.AddSingleton<WindowFocusBroker>();
    }

    /// <summary>
    /// Startup branch: <see cref="LocalConfigStore.HasManageConfig"/> → manage vs first-run.
    /// File I/O + lazy client construction only — no OCI List/Get until a button (Auto-detect)
    /// or MainViewModel poll. After Setup / Connect-existing writes <c>config.local.json</c>,
    /// call <see cref="ManageSession.ReloadFromDisk"/> so singleton clients and ViewModels
    /// rebind without restarting the process.
    /// </summary>
    private static void RegisterManageServices(IServiceCollection services)
    {
        var hasManageConfig = LocalConfigStore.HasManageConfig();
        var configHost = new LocalConfigHost();
        services.AddSingleton(configHost);
        services.AddSingleton(new HybridShell(
            hasManageConfig ? HybridShell.PageKind.Manage : HybridShell.PageKind.FirstRun));

        services.AddSingleton(sp => new ManageCloudServices(sp.GetRequiredService<LocalConfigHost>()));
        services.AddSingleton<ManageSession>();
        services.AddSingleton<ConnectExistingFlow>();
        services.AddSingleton<FirstRunViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<NotificationCenter>();
        services.AddSingleton<NotificationCenterViewModel>();
        services.AddSingleton<ChromeViewModel>();
        services.AddSingleton<WhitelistViewModel>();
        services.AddSingleton<UsageViewModel>();
        services.AddSingleton<ServerManagementViewModel>();
        services.AddSingleton<TroubleshootingViewModel>();
        services.AddSingleton<AdvancedViewModel>();
        services.AddSingleton<Vm1ShapeScaleViewModel>();
        services.AddSingleton<DestroyInfrastructureViewModel>();
        services.AddTransient<SetupWizardViewModel>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (!WebView2RuntimeGuard.IsMissingRuntime(e.Exception))
        {
            return;
        }

        // BlazorWebView initializes WebView2 asynchronously; missing Evergreen can surface here.
        e.Handled = true;
        WebView2RuntimeGuard.ShowMissingRuntimeMessage(e.Exception.Message);
        Shutdown();
    }
}
