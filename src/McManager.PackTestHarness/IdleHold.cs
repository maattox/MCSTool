using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.PackTestHarness;

/// <summary>
/// OS-ISSUE-7: Minecraft start runs record_boot.py, which force-enables idle.
/// Keep disabling during ReplacePackAsync so a long cobblemon boot cannot SoftStop VM1.
/// </summary>
internal static class IdleHold
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public static async Task DisableOnceAsync(
        ManagerLocalConfig config,
        CancellationToken cancellationToken)
    {
        var ssh = new SshService();
        await ssh.ApplyIdleSettingsAsync(
            config.Vm1,
            idleAgentEnabled: false,
            config.Budget.IdleTimeoutMinutes,
            config.Budget.BudgetWarnMinutes,
            cancellationToken);
    }

    public static async Task HoldUntilCancelledAsync(
        ManagerLocalConfig config,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DisableOnceAsync(config, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log?.Report("Idle hold disable failed: " + ex.Message);
            }

            try
            {
                await Task.Delay(Interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
