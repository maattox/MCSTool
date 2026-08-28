using Xunit;

namespace McManager.Core.Tests;

public sealed class PlayReservedIpHclTests
{
    [Fact]
    public void Reserved_play_ip_ignores_private_ip_id_after_create()
    {
        var tf = ReadRepoFile(Path.Combine("infra", "modules", "compute", "main.tf"));
        var play = SliceResource(tf, "oci_core_public_ip", "play");
        Assert.Contains("ignore_changes", play, StringComparison.Ordinal);
        Assert.Contains("private_ip_id", play, StringComparison.Ordinal);
        Assert.Contains("oci_core_private_ip.door_play.id", play, StringComparison.Ordinal);
    }

    [Fact]
    public void Promote_playable_verifies_assignment_on_vm1()
    {
        var script = ReadRepoFile(Path.Combine("door_vm", "scripts", "promote_playable.sh"));
        Assert.Contains("assigned-entity-id", script, StringComparison.Ordinal);
        Assert.Contains("VM1_PRIVATE_IP_ID", script, StringComparison.Ordinal);
        Assert.Contains("reserved IP is not on VM1", script, StringComparison.Ordinal);
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
