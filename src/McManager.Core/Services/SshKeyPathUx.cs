using McManager.Core.Config;
using McManager.Core.Setup;
using Renci.SshNet;

namespace McManager.Core.Services;

/// <summary>
/// Manage-mode SSH private-key paths on this PC. Game VM and doorbell can use
/// different files. Changing a path does not install a key on the guest.
/// </summary>
public static class SshKeyPathUx
{
    public const string HelpText =
        "Private keys stay on this PC and are never uploaded. The game VM and doorbell can use different files. "
        + "Pick the private key each VM already trusts — this does not install a new key on the guest.";

    public static string Normalize(string? path) => (path ?? "").Trim();

    public static bool PathsEqual(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool UsesSameFile(string? vm1Path, string? doorPath) =>
        PathsEqual(vm1Path, doorPath) && Normalize(vm1Path).Length > 0;

    public static string? InitialDirectory(string? storedPath)
    {
        var expanded = LocalConfigStore.ExpandPath(Normalize(storedPath));
        if (!string.IsNullOrWhiteSpace(expanded))
        {
            var dir = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                return dir;
        }

        var ssh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh");
        return Directory.Exists(ssh) ? ssh : null;
    }

    public static bool FileMissing(string? stored)
    {
        var path = LocalConfigStore.ExpandPath(Normalize(stored));
        return string.IsNullOrWhiteSpace(path) || !File.Exists(path);
    }

    public static ServiceResult ValidatePrivateKeyFile(string? path)
    {
        var stored = Normalize(path);
        if (stored.Length == 0)
            return ServiceResult.Fail("Choose an SSH private key file.");

        var expanded = LocalConfigStore.ExpandPath(stored);
        if (!File.Exists(expanded))
            return ServiceResult.Fail($"SSH key not found: {expanded}");

        try
        {
            var first = File.ReadLines(expanded)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
            if (!string.IsNullOrWhiteSpace(first) && SshKeyHelper.LooksLikePublicKey(first))
            {
                return ServiceResult.Fail(
                    "That file is an OpenSSH public key (.pub). Choose the private key (no .pub).");
            }

            using var keyFile = new PrivateKeyFile(expanded);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Could not read that file as an SSH private key: {ex.Message}");
        }
    }

    public static ServiceResult ValidatePair(string? vm1Path, string? doorPath)
    {
        var vm1 = ValidatePrivateKeyFile(vm1Path);
        if (!vm1.Succeeded)
            return ServiceResult.Fail("Game VM: " + (vm1.Error ?? "invalid SSH key."));

        var door = ValidatePrivateKeyFile(doorPath);
        if (!door.Succeeded)
            return ServiceResult.Fail("Door VM: " + (door.Error ?? "invalid SSH key."));

        return ServiceResult.Ok();
    }

    public static void Apply(ManagerLocalConfig config, string vm1Path, string doorPath)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Vm1.SshKeyPath = Normalize(vm1Path);
        config.Door.SshKeyPath = Normalize(doorPath);
    }
}
