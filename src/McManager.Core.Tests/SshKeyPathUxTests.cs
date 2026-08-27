using McManager.Core.Config;
using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SshKeyPathUxTests
{
    [Fact]
    public void Empty_path_is_rejected()
    {
        var result = SshKeyPathUx.ValidatePrivateKeyFile("  ");
        Assert.False(result.Succeeded);
        Assert.Contains("Choose an SSH private key", result.Error);
    }

    [Fact]
    public void Missing_file_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-missing-ssh-key-" + Guid.NewGuid().ToString("N"));
        var result = SshKeyPathUx.ValidatePrivateKeyFile(path);
        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Public_key_file_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-ssh-" + Guid.NewGuid().ToString("N") + ".pub");
        File.WriteAllText(path, "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIMcmgrTestPublicKeyBytesNotReal comment\n");
        try
        {
            var result = SshKeyPathUx.ValidatePrivateKeyFile(path);
            Assert.False(result.Succeeded);
            Assert.Contains("public key", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Garbage_file_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-ssh-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "not a key\n");
        try
        {
            var result = SshKeyPathUx.ValidatePrivateKeyFile(path);
            Assert.False(result.Succeeded);
            Assert.Contains("private key", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Apply_sets_independent_paths()
    {
        var config = new ManagerLocalConfig();
        SshKeyPathUx.Apply(config, @"C:\keys\vm1", @"C:\keys\door");
        Assert.Equal(@"C:\keys\vm1", config.Vm1.SshKeyPath);
        Assert.Equal(@"C:\keys\door", config.Door.SshKeyPath);
        Assert.False(SshKeyPathUx.UsesSameFile(config.Vm1.SshKeyPath, config.Door.SshKeyPath));
    }

    [Fact]
    public void Same_file_is_detected_case_insensitively()
    {
        Assert.True(SshKeyPathUx.UsesSameFile(@"C:\Keys\a", @"c:\keys\a"));
        Assert.False(SshKeyPathUx.UsesSameFile("", ""));
        Assert.True(SshKeyPathUx.PathsEqual("  a  ", "a"));
    }

    [Fact]
    public void File_missing_treats_empty_and_absent_as_missing()
    {
        Assert.True(SshKeyPathUx.FileMissing(""));
        Assert.True(SshKeyPathUx.FileMissing(Path.Combine(Path.GetTempPath(), "no-such-ssh-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Validate_pair_names_which_vm_failed()
    {
        var result = SshKeyPathUx.ValidatePair("", @"C:\missing-door-key");
        Assert.False(result.Succeeded);
        Assert.StartsWith("Game VM:", result.Error);
    }
}
