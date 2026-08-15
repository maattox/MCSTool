using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using McManager.App.ViewModels;
using McManager.App.Views;
using McManager.Core.Config;

namespace McManager.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Existing manage config → MainWindow (do not hijack every launch with Setup).
            desktop.MainWindow = LocalConfigStore.HasManageConfig()
                ? new MainWindow { DataContext = new MainViewModel() }
                : new FirstRunWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}