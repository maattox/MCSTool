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
        progress?.Report("Scanning OCI profiles for product stacks…");
        ServiceResult<ConnectExistingScanResult> scan;
        try
        {
            scan = await ConnectExistingService.ScanAsync(progress, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Auto-detect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        if (!scan.Succeeded || scan.Value is null)
        {
            progress?.Report(scan.Error ?? "Auto-detect failed.");
            await _dialogs.ShowInfoAsync("Auto-detect failed", scan.Error ?? "Unknown error.", cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        var result = scan.Value;
        var extra = result.Notes.Count == 0
            ? ""
            : "\n\nScan notes:\n- " + string.Join("\n- ", result.Notes.Take(12));

        if (result.Candidates.Count == 0)
        {
            progress?.Report("No product stacks found.");
            await _dialogs.ShowInfoAsync(
                "No existing stack found",
                "Auto-detect did not find a product compartment (name mcmgr / mcmgr-2 or tag mcmgr-domain=mc-server-compartment) "
                + "with meta/infra.json.\n\nUse Setup to deploy a new stack, or seed this server's config.local.json by hand."
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
            progress?.Report("Connect refused (incompatible infra schema).");
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
            "Existing infrastructure detected. Connect?",
            chosen.ConfirmSummary
            + "\n\nThis writes this server's config.local.json from meta/infra.json. "
            + "SSH private key path and RCON stay on this PC (not Object Storage).",
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
                "Replace local manage config?",
                "config.local.json already exists for this server. Connecting will overwrite OCIDs from the detected stack.\n\n"
                + "Existing SSH key path and RCON password on this PC will be kept unless you pick a new key.",
                confirmButtonText: "Overwrite",
                cancellationToken: cancellationToken);
            if (!overwrite)
            {
                progress?.Report("Connect cancelled (existing config kept).");
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

        progress?.Report("Writing local config from meta…");
        var hydrated = await ConnectExistingService.HydrateAsync(
            chosen,
            sshPath,
            preserve,
            rconPassword: preserve?.Rcon.Password,
            progress,
            cancellationToken);
        if (!hydrated.Succeeded || hydrated.Value is null)
        {
            progress?.Report(hydrated.Error ?? "Hydrate failed.");
            await _dialogs.ShowInfoAsync(
                "Connect failed",
                hydrated.Error ?? "Could not build local config.",
                cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        var saved = LocalConfigStore.SaveConfig(hydrated.Value);
        if (!saved.Succeeded)
        {
            progress?.Report(saved.Error ?? "Failed to save config.local.json.");
            await _dialogs.ShowInfoAsync(
                "Connect failed",
                saved.Error ?? "Could not write config.local.json for this server. Existing file was not deleted.",
                cancellationToken);
            return ConnectExistingOutcome.Failed;
        }

        progress?.Report("Connected. Local config written.");
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
            "Choose a stack to connect",
            "Multiple product stacks were found. Select one. This Manager connects to a single stack.",
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
                Title = "Select SSH private key (not stored in Object Storage)",
                Filters = [new FileTypeFilter("All files", ".*")],
            },
            cancellationToken);

        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
        {
            await _dialogs.ShowInfoAsync(
                "SSH key required",
                "Could not resolve the selected private key path. Connect did not write config.local.json.",
                cancellationToken);
            return null;
        }

        return path;
    }
}
