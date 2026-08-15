using System.Diagnostics;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Generate or import an OpenSSH public key for greenfield VMs.
/// New keys are <c>%USERPROFILE%\.ssh\mcmgr_ed25519_yyyyMMdd_HHmmss</c> so Setup never
/// reuses or overwrites a previous pair (including lab keys).
/// </summary>
public static class SshKeyHelper
{
    public const string DefaultKeyName = "mcmgr_ed25519";

    public static string DefaultPrivateKeyPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".ssh", DefaultKeyName);
    }

    public static string DefaultPublicKeyPath() => DefaultPrivateKeyPath() + ".pub";

    /// <summary>Unused unique private-key path under <c>~/.ssh</c> (timestamp + optional suffix).</summary>
    public static string NewPrivateKeyPath(DateTimeOffset? now = null)
    {
        var stamp = (now ?? DateTimeOffset.Now).ToString("yyyyMMdd_HHmmss");
        var sshDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh");
        Directory.CreateDirectory(sshDir);
        var path = Path.Combine(sshDir, $"mcmgr_ed25519_{stamp}");
        for (var n = 2; File.Exists(path) || File.Exists(path + ".pub"); n++)
            path = Path.Combine(sshDir, $"mcmgr_ed25519_{stamp}_{n}");
        return path;
    }

    public static bool LooksLikePublicKey(string line)
    {
        var t = line.Trim();
        return t.StartsWith("ssh-ed25519 ", StringComparison.Ordinal)
            || t.StartsWith("ssh-rsa ", StringComparison.Ordinal)
            || t.StartsWith("ssh-ed25519-sk ", StringComparison.Ordinal)
            || t.StartsWith("ecdsa-sha2-", StringComparison.Ordinal);
    }

    public static ServiceResult<SshPublicKeyInfo> ImportPublicKey(string path)
    {
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!File.Exists(path))
                return ServiceResult<SshPublicKeyInfo>.Fail($"Public key file not found: {path}");

            var line = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));

            if (string.IsNullOrWhiteSpace(line) || !LooksLikePublicKey(line))
            {
                return ServiceResult<SshPublicKeyInfo>.Fail(
                    "File does not look like an OpenSSH public key (expected ssh-ed25519 or ssh-rsa).");
            }

            var fingerprint = TryFingerprint(path) ?? "";
            return ServiceResult<SshPublicKeyInfo>.Ok(new SshPublicKeyInfo(path, line, fingerprint));
        }
        catch (Exception ex)
        {
            return ServiceResult<SshPublicKeyInfo>.Fail($"Failed to import public key: {ex.Message}");
        }
    }

    public static async Task<ServiceResult<SshPublicKeyInfo>> GenerateEd25519Async(
        CancellationToken cancellationToken = default)
    {
        var privatePath = NewPrivateKeyPath();
        var publicPath = privatePath + ".pub";
        var comment = Path.GetFileName(privatePath);

        try
        {
            var sshKeygen = ResolveSshKeygen();
            if (sshKeygen is null)
            {
                return ServiceResult<SshPublicKeyInfo>.Fail(
                    "ssh-keygen not found. Install OpenSSH Client (Windows Optional Features) or import an existing .pub.");
            }

            var psi = new ProcessStartInfo
            {
                FileName = sshKeygen,
                ArgumentList = { "-t", "ed25519", "-f", privatePath, "-N", "", "-C", comment },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return ServiceResult<SshPublicKeyInfo>.Fail("Failed to start ssh-keygen.");

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var err = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return ServiceResult<SshPublicKeyInfo>.Fail($"ssh-keygen failed: {err.Trim()}");
            }

            return ImportPublicKey(publicPath);
        }
        catch (Exception ex)
        {
            return ServiceResult<SshPublicKeyInfo>.Fail($"Failed to generate SSH key: {ex.Message}");
        }
    }

    public static string? TryFingerprint(string publicKeyPath)
    {
        try
        {
            var sshKeygen = ResolveSshKeygen();
            if (sshKeygen is null || !File.Exists(publicKeyPath))
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = sshKeygen,
                ArgumentList = { "-lf", publicKeyPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveSshKeygen()
    {
        var bundled = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "OpenSSH",
            "ssh-keygen.exe");
        if (File.Exists(bundled))
            return bundled;

        return "ssh-keygen";
    }
}

public sealed class SshPublicKeyInfo
{
    public SshPublicKeyInfo(string path, string publicKeyLine, string fingerprint)
    {
        Path = path;
        PublicKeyLine = publicKeyLine;
        Fingerprint = fingerprint;
    }

    public string Path { get; }
    public string PublicKeyLine { get; }
    public string Fingerprint { get; }
}
