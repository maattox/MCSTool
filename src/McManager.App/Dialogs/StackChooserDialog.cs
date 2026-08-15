using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using McManager.Core.Services;

namespace McManager.App.Dialogs;

public static class StackChooserDialog
{
    public static async Task<ConnectExistingCandidate?> ShowAsync(
        Window? owner,
        IReadOnlyList<ConnectExistingCandidate> candidates)
    {
        ConnectExistingCandidate? selected = null;
        var dialog = new Window
        {
            Title = "Choose a stack to connect",
            Width = 640,
            Height = 420,
            MinWidth = 480,
            MinHeight = 280,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
        };

        var list = new ListBox
        {
            ItemsSource = candidates,
            DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ConnectExistingCandidate.ChooserLabel)),
            MinHeight = 180,
        };
        if (candidates.Count > 0)
            list.SelectedIndex = 0;

        var connect = new Button { Content = "Continue", MinWidth = 88, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };

        connect.Click += (_, _) =>
        {
            selected = list.SelectedItem as ConnectExistingCandidate;
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Multiple product stacks were found. Select one. This Manager connects to a single stack.",
                    TextWrapping = TextWrapping.Wrap,
                },
                list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, connect },
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

        return selected;
    }
}
