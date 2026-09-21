using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PlayerMapPublicUrlTests
{
    [Fact]
    public void Default_port_is_player_http_80_not_admin_8080()
    {
        Assert.Equal(80, PlayerMapPublicUrl.DefaultPort);
        Assert.Equal(SecurityListIngressPlanner.PlayerHttpPort, PlayerMapPublicUrl.DefaultPort);
        Assert.NotEqual(PlayerMapPublicUrl.AdminHttpPort, PlayerMapPublicUrl.DefaultPort);
        Assert.Equal(8081, PlayerMapPublicUrl.FallbackPort);
    }

    [Fact]
    public void Formats_door_ephemeral_url_not_play_ip()
    {
        const string doorEphemeral = "203.0.113.25";
        const string playIp = "198.51.100.7";

        var url = PlayerMapPublicUrl.TryFormat(doorEphemeral);

        Assert.Equal("http://203.0.113.25/", url);
        Assert.DoesNotContain(playIp, url);
        Assert.DoesNotContain(":8080", url);
        Assert.DoesNotContain(":25565", url);
    }

    [Fact]
    public void Fallback_8081_includes_the_port()
    {
        var url = PlayerMapPublicUrl.TryFormat("203.0.113.25", PlayerMapPublicUrl.FallbackPort);
        Assert.Equal("http://203.0.113.25:8081/", url);
        Assert.DoesNotContain(":8080", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://203.0.113.25")]
    public void Missing_or_scheme_host_is_null(string? host)
    {
        Assert.Null(PlayerMapPublicUrl.TryFormat(host));
    }
}
