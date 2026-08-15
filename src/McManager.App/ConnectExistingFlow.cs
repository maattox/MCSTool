using Avalonia.Controls;
using Avalonia.Platform.Storage;
using McManager.App.Dialogs;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.App.Views;

public enum ConnectExistingOutcome
{
    Cancelled,
    NoneFound,
    Failed,
    Connected,
}

/// <summary>
/// Shared First-run / Advanced Connect-existing UI: scan → chooser → confirm → hydrate.
/// Never invoked from app startup.
/// </summary>
public static class ConnectExistingFlow
{
    public static async Task<ConnectExistingOutcome> RunAsync(
        Window owner,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Scanning OCI profiles for product stacks…");
        ServiceResult<ConnectExistingScanResult> scan;
        try
        {
            scan = await ConnectExistingService.ScanAsync(progress, cancellationToken: cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            progress?.Report("Auto-detect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        if (!scan.Succeeded || scan.Value is null)
        {
            progress?.Report(scan.Error ?? "Auto-detect failed.");
            await InfoDialog.ShowAsync(owner, "Auto-detect failed", scan.Error ?? "Unknown error.");
            return ConnectExistingOutcome.Failed;
        }

        var result = scan.Value;
        var extra = result.Notes.Count == 0
            ? ""
            : "\n\nScan notes:\n- " + string.Join("\n- ", result.Notes.Take(12));

        if (result.Candidates.Count == 0)
        {
            progress?.Report("No product stacks found.");
            await InfoDialog.ShowAsync(
                owner,
                "No existing stack found",
                "Auto-detect did not find a product compartment (name mcmgr or tag mcmgr-domain=mc-server-compartment) "
                + "with meta/infra.json.\n\nUse Setup to deploy a new stack, or seed data/config.local.json by hand."
                + extra);
            return ConnectExistingOutcome.NoneFound;
        }

        ConnectExistingCandidate? chosen = result.Candidates.Count == 1
            ? result.Candidates[0]
            : await StackChooserDialog.ShowAsync(owner, result.Candidates);
        if (chosen is null)
        {
            progress?.Report("Connect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        if (chosen.HasSchemaWarning)
        {
            var schemaOk = await ConfirmDialog.ShowAsync(
                owner,
                "Schema warning — connect anyway?",
                chosen.ConfirmSummary
                + "\n\nThis Manager will not modify Object Storage meta or the cloud stack. Continue?",
                confirmButtonText: "Connect anyway");
            if (!schemaOk)
            {
                progress?.Report("Connect cancelled (schema warning).");
                return ConnectExistingOutcome.Cancelled;
            }
        }

        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            "Existing infrastructure detected. Connect?",
            chosen.ConfirmSummary
            + "\n\nThis writes data/config.local.json from meta/infra.json. "
            + "SSH private key path and RCON stay on this PC (not Object Storage).",
            confirmButtonText: "Connect");
        if (!confirmed)
        {
            progress?.Report("Connect cancelled.");
            return ConnectExistingOutcome.Cancelled;
        }

        ManagerLocalConfig? preserve = null;
        if (LocalConfigStore.ConfigFileExists())
        {
            var overwrite = await ConfirmDialog.ShowAsync(
                owner,
                "Replace local manage config?",
                "data/config.local.json already exists. Connecting will overwrite OCIDs from the detected stack.\n\n"
                + "Existing SSH key path and RCON password on this PC will be kept unless you pick a new key.\n\n"
                + "To avoid clobbering a working seed, set MCMANAGER_CONFIG_DIR to a new empty folder.",
                confirmButtonText: "Overwrite");
            if (!overwrite)
            {
                progress?.Report("Connect cancelled (existing config kept).");
                return ConnectExistingOutcome.Cancelled;
            }

            var loaded = LocalConfigStore.Load();
            if (loaded.Succeeded)
                preserve = loaded.Config;
        }

        var sshPath = await ResolveSshKeyPathAsync(owner, preserve).ConfigureAwait(true);
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
                cancellationToken)
            .ConfigureAwait(true);
        if (!hydrated.Succeeded || hydrated.Value is null)
        {
            progress?.Report(hydrated.Error ?? "Hydrate failed.");
            await InfoDialog.ShowAsync(owner, "Connect failed", hydrated.Error ?? "Could not build local config.");
            return ConnectExistingOutcome.Failed;
        }

        var saved = LocalConfigStore.SaveConfig(hydrated.Value);
        if (!saved.Succeeded)
        {
            progress?.Report(saved.Error ?? "Failed to save config.local.json.");
            await InfoDialog.ShowAsync(
                owner,
                "Connect failed",
                saved.Error ?? "Could not write data/config.local.json. Existing file was not deleted.");
            return ConnectExistingOutcome.Failed;
        }

        progress?.Report("Connected. Local config written.");
        return ConnectExistingOutcome.Connected;
    }

    private static async Task<string?> ResolveSshKeyPathAsync(Window owner, ManagerLocalConfig? preserve)
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

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select SSH private key (not stored in Object Storage)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("SSH private keys") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return null;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            await InfoDialog.ShowAsync(
                owner,
                "SSH key required",
                "Could not resolve the selected private key path. Connect did not write config.local.json.");
            return null;
        }

        return path;
    }
}
