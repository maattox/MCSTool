using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace McManager.App.Dialogs;

public enum CapacityWaitChoice
{
    Dismissed,
    RetryNow,
    AutoRetry,
}

public static class CapacityWaitDialog
{
    public const string Explanation =
        "Always Free A1 Flex host capacity is unavailable in this region right now. VM1 was not created.\n\n"
        + "Other Always Free resources from this Setup (compartment, VCN, door Micro, reserved IP, IAM) may already exist. Retry reuses them; it does not start from scratch.\n\n"
        + "Try again now, or auto-retry every 5 minutes while Setup stays open. Auto-retry checks capacity first and stays silent on later failures.\n\n"
        + "Close returns to Setup so you can pause later or resume another time.";

    public static async Task<CapacityWaitChoice> ShowAsync(Window? owner)
    {
        var choice = CapacityWaitChoice.Dismissed;
        var dialog = new Window
        {
            Title = "Always Free capacity unavailable",
            Width = 520,
            MinHeight = 240,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
        };

        var retry = new Button
        {
            Content = "Try again now",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 32,
        };
        var autoRetry = new Button
        {
            Content = "Auto-retry every 5 minutes",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 32,
        };
        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            MinHeight = 32,
            IsCancel = true,
        };

        retry.Click += (_, _) =>
        {
            choice = CapacityWaitChoice.RetryNow;
            dialog.Close();
        };
        autoRetry.Click += (_, _) =>
        {
            choice = CapacityWaitChoice.AutoRetry;
            dialog.Close();
        };
        close.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = Explanation,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Spacing = 8,
                    Children = { retry, autoRetry, close },
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

        return choice;
    }
}
