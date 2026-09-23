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
        Assert.Contains("${BASE}_nether", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("${BASE}_the_end", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("/var/lib/mcmgr-map", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("$MAP/render", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("$MAP/http", plan.RemoteScript, StringComparison.Ordinal);
        Assert.Contains("systemctl stop mc-player-map.service", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$MAP/viewer", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("repair-permissions", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("mods/", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("server.properties", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("backups/", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("objectstorage", plan.RemoteScript, StringComparison.OrdinalIgnoreCase);
        // Minecraft start is SshService.WipeWorld after this script.
        Assert.DoesNotContain("systemctl start", plan.RemoteScript, StringComparison.Ordinal);
        Assert.DoesNotContain("systemctl stop minecraft", plan.RemoteScript, StringComparison.Ordinal);
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

    [Fact]
    public void Door_reset_clears_tiles_and_pins_and_keeps_an_empty_page()
    {
        var script = PlayerMapReset.DoorClearScript();
        Assert.Contains("/var/lib/mc-player-map", script, StringComparison.Ordinal);
        Assert.Contains("player-map-markers.json", script, StringComparison.Ordinal);
        Assert.Contains("player-map.sha256", script, StringComparison.Ordinal);
        Assert.Contains("Map is not ready yet.", script, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/mcmgr", script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf -- /", script, StringComparison.Ordinal);
    }
}
