using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using McManager.App.ViewModels;

namespace McManager.App.Views;

public partial class MainWindow : Window
{
    private int _lastMainTabIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) => (DataContext as MainViewModel)?.SetWindowFocused(true);
        Deactivated += (_, _) => (DataContext as MainViewModel)?.SetWindowFocused(false);
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    /// <summary>After Connect-existing writes config, replace the current window with a fresh manage UI.</summary>
    public static void ShowReplacing(Window current)
    {
        var main = new MainWindow
        {
            DataContext = new MainViewModel(),
        };

        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = main;

        main.Show();
        if (!ReferenceEquals(current, main))
            current.Close();
    }

    private void OnMainTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // ListBox/other nested Selectors also raise SelectionChanged that bubbles to TabControl.
        // Only react when the selected tab index actually changes.
        if (sender is not TabControl tabs || DataContext is not MainViewModel vm)
            return;
        if (tabs.SelectedIndex == _lastMainTabIndex)
            return;

        _lastMainTabIndex = tabs.SelectedIndex;
        vm.OnMainTabChanged(tabs.SelectedIndex);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty
            && change.NewValue is WindowState state
            && DataContext is MainViewModel vm)
        {
            vm.SetWindowFocused(state != WindowState.Minimized && IsActive);
        }
    }
}
