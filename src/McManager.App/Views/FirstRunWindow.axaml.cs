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

    private async void OnDetectClick(object? sender, RoutedEventArgs e)
    {
        SetBusy(true);
        StatusText.Text = "Scanning…";
        var progress = new Progress<string>(msg => StatusText.Text = msg);
        try
        {
            var outcome = await ConnectExistingFlow.RunAsync(this, progress);
            if (outcome == ConnectExistingOutcome.Connected)
                MainWindow.ShowReplacing(this);
        }
        finally
        {
            SetBusy(false);
        }
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

    private void SetBusy(bool busy)
    {
        SetupButton.IsEnabled = !busy;
        DetectButton.IsEnabled = !busy;
        ExistingButton.IsEnabled = !busy;
    }
}
