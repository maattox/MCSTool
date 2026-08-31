using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupDualSshTests
{
    [Fact]
    public void Default_seeds_the_same_private_path_on_both_vms()
    {
        var outputs = TofuApplyOutputs.Parse(TofuApplyOutputs.CannedDryRunJson).Value!;
        var state = new SetupWizardState
        {
            SshPublicKeyPath = @"C:\keys\game.pub",
            SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIgame testhost",
        };

        var config = outputs.ToLocalConfig(state, "rcon");

        Assert.Equal(@"C:\keys\game", config.Vm1.SshKeyPath);
        Assert.Equal(@"C:\keys\game", config.Door.SshKeyPath);
        Assert.False(TofuApplyOutputs.UsesSplitDoorKey(state));
        Assert.Equal(state.SshPublicKey, TofuApplyOutputs.DoorPublicKeyLine(state));
    }

    [Fact]
    public void Split_seeds_distinct_vm1_and_door_private_paths()
    {
        var outputs = TofuApplyOutputs.Parse(TofuApplyOutputs.CannedDryRunJson).Value!;
        var state = new SetupWizardState
        {
            SshPublicKeyPath = @"C:\keys\game.pub",
            SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIgame testhost",
            SshSplitDoorKey = true,
            DoorSshPublicKeyPath = @"C:\keys\door.pub",
            DoorSshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIdoor testhost",
        };

        var config = outputs.ToLocalConfig(state, "rcon");

        Assert.Equal(@"C:\keys\game", config.Vm1.SshKeyPath);
        Assert.Equal(@"C:\keys\door", config.Door.SshKeyPath);
        Assert.True(TofuApplyOutputs.UsesSplitDoorKey(state));
        Assert.Equal(state.DoorSshPublicKey, TofuApplyOutputs.DoorPublicKeyLine(state));
    }

    [Fact]
    public void Split_without_door_key_falls_back_to_game_vm_key()
    {
        var state = new SetupWizardState
        {
            SshPublicKeyPath = @"C:\keys\game.pub",
            SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIgame testhost",
            SshSplitDoorKey = true,
        };

        Assert.False(TofuApplyOutputs.UsesSplitDoorKey(state));
        Assert.Equal(@"C:\keys\game", TofuApplyOutputs.DoorPrivateKeyPath(state));
        Assert.Equal(state.SshPublicKey, TofuApplyOutputs.DoorPublicKeyLine(state));
    }

    [Fact]
    public void Plan_summary_names_both_keys_when_split()
    {
        var text = InfraPlanSummary.Build(new SetupWizardState
        {
            SshFingerprint = "SHA256:game",
            SshSplitDoorKey = true,
            DoorSshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIdoor testhost",
            DoorSshFingerprint = "SHA256:door",
        });

        Assert.Contains("SSH: game VM SHA256:game; door SHA256:door", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Door_hcl_uses_optional_door_public_key()
    {
        var root = ReadRepoFile(Path.Combine("infra", "variables.tf"));
        Assert.Contains("variable \"door_ssh_public_key\"", root, StringComparison.Ordinal);

        var compute = ReadRepoFile(Path.Combine("infra", "modules", "compute", "main.tf"));
        Assert.Contains("door_ssh_authorized_keys", compute, StringComparison.Ordinal);
        Assert.Contains("var.door_ssh_public_key", compute, StringComparison.Ordinal);

        var door = SliceResource(compute, "oci_core_instance", "door");
        Assert.Contains("local.door_ssh_authorized_keys", door, StringComparison.Ordinal);
        Assert.DoesNotContain("ssh_authorized_keys = var.ssh_public_key", door, StringComparison.Ordinal);
    }

    private static string SliceResource(string tf, string type, string name)
    {
        var header = $"resource \"{type}\" \"{name}\"";
        var start = tf.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {header}");
        var brace = tf.IndexOf('{', start);
        Assert.True(brace >= 0);
        var depth = 0;
        for (var i = brace; i < tf.Length; i++)
        {
            if (tf[i] == '{')
                depth++;
            else if (tf[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return tf[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unclosed {header}");
    }

    private static string ReadRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not find " + relative + " walking up from " + AppContext.BaseDirectory);
    }
}
