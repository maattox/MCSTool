using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ServerPluginsInspectTests
{
    [Fact]
    public void Remote_script_lists_plugins_jars_only()
    {
        var script = ServerPluginsInspect.RemoteScript;
        Assert.Contains("/opt/mcmgr/server/plugins", script, StringComparison.Ordinal);
        Assert.Contains("HOME=\"${HOME:-/home/ubuntu}\"", script, StringComparison.Ordinal);
        Assert.Contains("-name '*.jar'", script, StringComparison.Ordinal);
        Assert.Contains("sudo bash -c", ServerPluginsInspect.RemoteCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("/reload", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parses_jar_names()
    {
        var stdout =
            """
            ---PLUGINS---
            spark.jar
            luckperms.jar
            MCMGR_PLUGINS_OK
            """;

        Assert.True(ServerPluginsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.False(result.PluginsDirectoryMissing);
        Assert.Equal(["luckperms.jar", "spark.jar"], result.FileNames.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Contains("2 plugin jars", result.SummaryLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_missing_directory()
    {
        var stdout = "---PLUGINS---\nMCMGR_PLUGINS_MISSING\n";
        Assert.True(ServerPluginsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.True(result.PluginsDirectoryMissing);
        Assert.Empty(result.FileNames);
    }

    [Fact]
    public void Safe_jar_name_rejects_paths()
    {
        Assert.False(ServerPluginsInspect.IsSafeJarName("../x.jar"));
        Assert.False(ServerPluginsInspect.IsSafeJarName("a/b.jar"));
        Assert.False(ServerPluginsInspect.IsSafeJarName("notes.txt"));
        Assert.True(ServerPluginsInspect.IsSafeJarName("spark.jar"));
    }

    [Fact]
    public void Install_and_delete_are_elevated_mcmgr_install()
    {
        var install = ServerPluginsInspect.InstallScript("spark.jar");
        Assert.Contains("sudo bash -c", install, StringComparison.Ordinal);
        Assert.Contains("install -o mcmgr -g mcmgr -m 0640", install, StringComparison.Ordinal);
        Assert.Contains("/tmp/mcmgr-plugin-upload/spark.jar", install, StringComparison.Ordinal);
        Assert.DoesNotContain("/reload", install, StringComparison.OrdinalIgnoreCase);

        var del = ServerPluginsInspect.DeleteScript("spark.jar");
        Assert.Contains("sudo bash -c", del, StringComparison.Ordinal);
        Assert.Contains("rm -f", del, StringComparison.Ordinal);
        Assert.Contains("/opt/mcmgr/server/plugins", del, StringComparison.Ordinal);
    }
}
