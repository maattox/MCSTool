using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackCopyRemoteTests
{
    [Fact]
    public void Apply_replaces_mods_before_merge_copy()
    {
        var cmd = PackCopyRemote.ApplyStagedTreeCommand("/tmp/mcmgr-pack", "/tmp/mcmgr-onbox");
        Assert.Contains("rm -rf /opt/mcmgr/server/mods", cmd, StringComparison.Ordinal);
        Assert.Contains("mkdir -p /opt/mcmgr/server/mods", cmd, StringComparison.Ordinal);
        Assert.Contains("cp -a /tmp/mcmgr-pack/. /opt/mcmgr/server/", cmd, StringComparison.Ordinal);
        Assert.Contains("bash /tmp/mcmgr-onbox/repair-permissions.sh", cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("mods.quarantined", cmd, StringComparison.Ordinal);
        var rmAt = cmd.IndexOf("rm -rf /opt/mcmgr/server/mods", StringComparison.Ordinal);
        var cpAt = cmd.IndexOf("cp -a /tmp/mcmgr-pack/.", StringComparison.Ordinal);
        Assert.True(rmAt >= 0 && cpAt > rmAt, "mods/ must be cleared before cp -a merge");
    }
}
