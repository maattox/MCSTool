using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using McManager.App.ViewModels;
using McManager.Core.Config;

namespace McManager.App.Views;

public partial class SetupWizardWindow : Window
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closing += OnClosing;
    }

    /// <summary>First-run: become the main window and close the chooser so Setup is not a second window.</summary>
    public static void ShowReplacing(Window current)
    {
        var vm = new SetupWizardViewModel();
        var window = new SetupWizardWindow
        {
            DataContext = vm,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        vm.Host = window;

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;

        window.Show();
        if (!ReferenceEquals(current, window))
            current.Close();
    }

    public static async Task ShowAsync(Window? owner)
    {
        var vm = new SetupWizardViewModel();
        var window = new SetupWizardWindow
        {
            DataContext = vm,
        };
        vm.Host = window;

        if (owner is not null)
            await window.ShowDialog(owner);
        else
            window.Show();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
        {
            vm.Host = this;
            await vm.InitializeAsync();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
            vm.PrepareToClose();
        RestoreNextMainWindow();
    }

    private void RestoreNextMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;
        if (!ReferenceEquals(desktop.MainWindow, this))
            return;

        Window next = LocalConfigStore.HasManageConfig()
            ? new MainWindow { DataContext = new MainViewModel() }
            : new FirstRunWindow();
        desktop.MainWindow = next;
        next.Show();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
