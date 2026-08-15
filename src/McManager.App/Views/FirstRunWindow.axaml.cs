using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using McManager.App.ViewModels;

namespace McManager.App.Views;

public partial class FirstRunWindow : Window
{
    public FirstRunWindow()
    {
        InitializeComponent();
    }

    private void OnSetupClick(object? sender, RoutedEventArgs e)
    {
        SetupWizardWindow.ShowReplacing(this);
    }

    private void OnExistingClick(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var main = new MainWindow
        {
            DataContext = new MainViewModel(),
        };
        desktop.MainWindow = main;
        main.Show();
        Close();
    }
}
