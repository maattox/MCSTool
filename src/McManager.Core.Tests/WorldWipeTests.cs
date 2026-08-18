using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class WorldWipeTests
{
    [Theory]
    [InlineData("/opt/mcmgr/server/world")]
    [InlineData("/opt/mcmgr/server/world/")]
    [InlineData(" /opt/mcmgr/server/world ")]
    [InlineData("/opt/mcmgr/server/MyWorld")]
    public void Accepts_world_folder_under_server_dir(string worldPath)
    {
        Assert.True(WorldWipe.TryCreate(worldPath, out var plan, out var error), error);
        Assert.StartsWith("/opt/mcmgr/server/", plan.WorldPath, StringComparison.Ordinal);
        Assert.DoesNotContain("..", plan.WorldPath, StringComparison.Ordinal);
        Assert.Contains($"WORLD='{plan.WorldPath}'", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("rm -rf -- \"$WORLD\"", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("mkdir -p -- \"$WORLD\"", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("chown mcmgr:mcmgr -- \"$WORLD\"", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("mods/", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("server.properties", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("backups/", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("objectstorage", plan.RemoteScript, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("opt/mcmgr/server/world")]
    [InlineData("C:/opt/mcmgr/server/world")]
    [InlineData("/opt/mcmgr/server")]
    [InlineData("/opt/mcmgr/server/")]
    [InlineData("/opt/mcmgr")]
    [InlineData("/")]
    [InlineData("/tmp/world")]
    [InlineData("/opt/mcmgr/server/../world")]
    [InlineData("/opt/mcmgr/server/world/region")]
    [InlineData("/opt/mcmgr/server/mods")]
    [InlineData("/opt/mcmgr/server/config")]
    [InlineData("/opt/mcmgr/server/server.properties")]
    [InlineData("/opt/mcmgr/server/world;rm -rf /")]
    public void Rejects_unsafe_or_non_world_paths(string? worldPath)
    {
        Assert.False(WorldWipe.TryCreate(worldPath, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Default_local_config_world_path_is_the_live_save()
    {
        const string fromLocalConfig = "/opt/mcmgr/server/world";
        Assert.True(WorldWipe.TryNormalizeWorldPath(fromLocalConfig, out var path, out var error), error);
        Assert.Equal("/opt/mcmgr/server/world", path);
    }
}
