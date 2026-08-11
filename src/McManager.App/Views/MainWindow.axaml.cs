using Avalonia;
using Avalonia.Controls;
using McManager.App.ViewModels;

namespace McManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Activated += (_, _) => (DataContext as MainViewModel)?.SetWindowFocused(true);
        Deactivated += (_, _) => (DataContext as MainViewModel)?.SetWindowFocused(false);
        Closed += (_, _) => (DataContext as MainViewModel)?.Dispose();
    }

    private void OnMainTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabs && DataContext is MainViewModel vm)
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
