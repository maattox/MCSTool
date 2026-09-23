using System.IO;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid;

public enum ConnectExistingOutcome
{
    Cancelled,
    NoneFound,
    Failed,
    Incompatible,
    Connected,
}

/// <summary>
/// Shared First-run / Advanced Connect-existing UI: scan → chooser → confirm → hydrate.
/// Never invoked from app startup — Auto-detect is button-gated only.
/// </summary>
public sealed class ConnectExistingFlow
{
    private readonly IUiDialogs _dialogs;
    private readonly IFilePicker _filePicker;

    public ConnectExistingFlow(IUiDialogs dialogs, IFilePicker filePicker)
    {
        _dialogs = dialogs;
        _filePicker = filePicker;
    }

    public async Task<ConnectExistingOutcome> RunAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Looking for MCSTool servers in your Oracle account…");
        ServiceResult<ConnectExistingScanResult> scan;
        try
        {
            scan = await ConnectExistingService.ScanAsync(progress, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Cancelled. Nothing was changed.");
            return ConnectExistingOutcome.Cancelled;
        }

        if (!scan.Succeeded || scan.Value is null)
        {
            progress?.Report(scan.Error ?? "Finding an existing server failed.");
            await _dialogs.ShowInfoAsync("Could not find servers", scan.Error ?? "Unknown error.", cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        var result = scan.Value;
        var extra = result.Notes.Count == 0
            ? ""
            : "\n\nDetails:\n- " + string.Join("\n- ", result.Notes.Take(12));

        if (result.Candidates.Count == 0)
        {
            progress?.Report("No MCSTool server found.");
            await _dialogs.ShowInfoAsync(
                "No server found",
                "No MCSTool server was found in this Oracle account. Use Setup to create one, or copy this server's settings onto this PC."
                + extra,
                cancellationToken);
            return ConnectExistingOutcome.NoneFound;
        }

        var chosen = result.Candidates.Count == 1
            ? result.Candidates[0]
            : await ChooseStackAsync(result.Candidates, cancellationToken);
        if (chosen is null)
        {
            progress?.Report("Connect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        var compatibility = chosen.Compatibility;
        if (compatibility.BlocksConnect)
        {
            progress?.Report("Can't connect: that server is incompatible with this version of MCSTool.");
            await _dialogs.ShowInfoAsync(
                compatibility.DialogTitle,
                compatibility.FormatBody(chosen.IdentitySummary),
                cancellationToken);
            return ConnectExistingOutcome.Incompatible;
        }

        if (compatibility.RequiresConfirm)
        {
            var schemaOk = await _dialogs.ConfirmAsync(
                compatibility.DialogTitle,
                compatibility.FormatBody(chosen.IdentitySummary),
                confirmButtonText: "Connect anyway",
                cancellationToken: cancellationToken);
            if (!schemaOk)
            {
                progress?.Report("Connect cancelled (version warning).");
                return ConnectExistingOutcome.Cancelled;
            }
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "MCSTool server found. Connect?",
            chosen.ConfirmSummary
            + "\n\nMCSTool will save this server's settings on this PC. "
            + "Your SSH key and console password stay on this PC.",
            confirmButtonText: "Connect",
            cancellationToken: cancellationToken);
        if (!confirmed)
        {
            progress?.Report("Connect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        ManagerLocalConfig? preserve = null;
        if (LocalConfigStore.ConfigFileExists())
        {
            var overwrite = await _dialogs.ConfirmAsync(
                "Replace saved settings?",
                "This PC already has saved settings for a server. Connecting replaces them with the server you found.\n\n"
                + "Your SSH key and console password on this PC are kept unless you pick a new key.",
                confirmButtonText: "Replace",
                cancellationToken: cancellationToken);
            if (!overwrite)
            {
                progress?.Report("Connect cancelled (saved settings kept).");
                return ConnectExistingOutcome.Cancelled;
            }

            var loaded = LocalConfigStore.Load();
            if (loaded.Succeeded)
                preserve = loaded.Config;
        }

        var sshPath = await ResolveSshKeyPathAsync(preserve, cancellationToken);
        if (sshPath is null)
        {
            progress?.Report("Connect cancelled (no SSH key).");
            return ConnectExistingOutcome.Cancelled;
        }

        progress?.Report("Saving settings…");
        var hydrated = await ConnectExistingService.HydrateAsync(
            chosen,
            sshPath,
            preserve,
            rconPassword: preserve?.Rcon.Password,
            progress,
            cancellationToken);
        if (!hydrated.Succeeded || hydrated.Value is null)
        {
            progress?.Report(hydrated.Error ?? "Could not save settings.");
            await _dialogs.ShowInfoAsync(
                "Connect failed",
                hydrated.Error ?? "Could not save settings.",
                cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        var saved = LocalConfigStore.SaveConfig(hydrated.Value);
        if (!saved.Succeeded)
        {
            progress?.Report(saved.Error ?? "Failed to save settings.");
            await _dialogs.ShowInfoAsync(
                "Connect failed",
                saved.Error ?? "Could not save settings for this server. Your existing settings were not deleted.",
                cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        progress?.Report("Connected. Settings saved.");
        return ConnectExistingOutcome.Connected;
    }

    /// <summary>
    /// Stack chooser when Auto-detect finds more than one product stack.
    /// Hosted by B3 <see cref="IUiDialogs.ChooseAsync"/> (Razor overlay), not a second window.
    /// </summary>
    private async Task<ConnectExistingCandidate?> ChooseStackAsync(
        IReadOnlyList<ConnectExistingCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var choices = candidates
            .Select((c, i) => new UiChoice(i.ToString(), c.ChooserLabel))
            .ToList();
        var id = await _dialogs.ChooseAsync(
            "Choose a server",
            "More than one MCSTool server was found. MCSTool connects to one at a time.",
            choices,
            cancellationToken);
        if (id is null
            || !int.TryParse(id, out var index)
            || index < 0
            || index >= candidates.Count)
        {
            return null;
        }

        return candidates[index];
    }

    private async Task<string?> ResolveSshKeyPathAsync(
        ManagerLocalConfig? preserve,
        CancellationToken cancellationToken)
    {
        var existing = preserve?.Vm1.SshKeyPath;
        if (string.IsNullOrWhiteSpace(existing))
            existing = preserve?.Door.SshKeyPath;

        if (!string.IsNullOrWhiteSpace(existing))
        {
            var expanded = LocalConfigStore.ExpandPath(existing);
            if (File.Exists(expanded))
                return existing;
        }

        var path = await _filePicker.OpenFileAsync(
            new FilePickRequest
            {
                Title = "Select SSH private key",
                Filters = [new FileTypeFilter("All files", ".*")],
            },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
        {
            await _dialogs.ShowInfoAsync(
                "SSH key required",
                "Could not find the selected private key. Nothing was changed.",
                cancellationToken);
            return null;
        }

        return path;
    }
}
