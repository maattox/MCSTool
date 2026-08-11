using Avalonia;
using Avalonia.Controls;
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
