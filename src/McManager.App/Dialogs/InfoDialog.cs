using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace McManager.App.Dialogs;

public static class InfoDialog
{
    public static async Task ShowAsync(Window? owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 520,
            MinHeight = 160,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
        };

        var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true, IsCancel = true };
        ok.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { ok },
                },
            },
        };

        if (owner is not null)
            await dialog.ShowDialog(owner);
        else
        {
            var tcs = new TaskCompletionSource();
            dialog.Closed += (_, _) => tcs.TrySetResult();
            dialog.Show();
            await tcs.Task;
        }
    }
}
