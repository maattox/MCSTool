using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class OnboxDriverExportsTests
{
    [Fact]
    public void Build_modded_fabric_26_includes_java_major_25()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            MinecraftVersion = "26.2",
            PackLoader = "fabric",
            PackLoaderVersion = "0.18.0",
            EulaAccepted = true,
        };

        var exports = OnboxDriverExports.Build(state, analyzedJavaMajor: 25);

        Assert.Contains("DISTRIBUTION='fabric'", exports, StringComparison.Ordinal);
        Assert.Contains("MINECRAFT_VERSION='26.2'", exports, StringComparison.Ordinal);
        Assert.Contains("JAVA_MAJOR=25", exports, StringComparison.Ordinal);
        Assert.Contains("LOADER_VERSION='0.18.0'", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_uses_minecraft_floor_when_analyzed_java_omitted()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            MinecraftVersion = "26.1",
            PackLoader = "fabric",
            PackLoaderVersion = "0.17.2",
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.Contains("JAVA_MAJOR=25", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_analyzed_java_overrides_floor()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            MinecraftVersion = "1.21.1",
            PackLoader = "fabric",
            PackLoaderVersion = "0.17.2",
        };

        var exports = OnboxDriverExports.Build(state, analyzedJavaMajor: 25);

        Assert.Contains("JAVA_MAJOR=25", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_pack_java_major_on_state_wins_over_floor()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            MinecraftVersion = "1.21.1",
            PackLoader = "fabric",
            PackLoaderVersion = "0.17.2",
            PackJavaMajor = 25,
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.Contains("JAVA_MAJOR=25", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_vanilla_omits_java_major_when_version_unknown()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            MinecraftVersion = "not-a-version",
            EulaAccepted = true,
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.DoesNotContain("JAVA_MAJOR=", exports, StringComparison.Ordinal);
    }
}
