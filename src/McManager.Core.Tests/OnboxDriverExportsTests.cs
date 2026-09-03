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
        Assert.Contains("JVM_XMS='4G'", exports, StringComparison.Ordinal);
        Assert.Contains("JVM_XMX='4G'", exports, StringComparison.Ordinal);
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

    [Fact]
    public void Build_exports_matched_heap_preset()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            MinecraftVersion = "1.21.8",
            JvmXmx = "6G",
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.Contains("JVM_XMS='6G'", exports, StringComparison.Ordinal);
        Assert.Contains("JVM_XMX='6G'", exports, StringComparison.Ordinal);
        Assert.DoesNotContain("JVM_XMS='2G'", exports, StringComparison.Ordinal);
        Assert.DoesNotContain("LEVEL_SEED=", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_omits_level_seed_when_blank()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            MinecraftVersion = "1.21.8",
            WorldSeed = "   ",
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.DoesNotContain("LEVEL_SEED=", exports, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_exports_trimmed_level_seed()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            MinecraftVersion = "1.21.8",
            WorldSeed = "  MySeed  ",
        };

        var exports = OnboxDriverExports.Build(state);

        Assert.Contains("LEVEL_SEED='MySeed'", exports, StringComparison.Ordinal);
    }
}
