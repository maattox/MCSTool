using System.Windows;
using McManager.Hybrid.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace McManager.Hybrid;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Resources.Add("services", App.Services);

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
}
