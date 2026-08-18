using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ProgramPathsTests
{
    [Fact]
    public void Describe_uses_the_data_directory_and_oci_file()
    {
        var data = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(data, LocalConfigStore.ConfigFileName), "{}");
            var oci = Path.Combine(data, "oci-config");
            File.WriteAllText(oci, "[DEFAULT]");

            var rows = ProgramPaths.Describe(data, oci);
            Assert.Equal(4, rows.Count);

            var folder = rows.Single(r => r.Id == "data");
            Assert.Equal(data, folder.Path);
            Assert.True(folder.Exists);

            var config = rows.Single(r => r.Id == "config");
            Assert.Equal(Path.Combine(data, "config.local.json"), config.Path);
            Assert.True(config.Exists);

            var tofu = rows.Single(r => r.Id == "tofu");
            Assert.Equal(TofuWorkspace.TofuRootDirectory(), tofu.Path);

            var api = rows.Single(r => r.Id == "oci");
            Assert.Equal(oci, api.Path);
            Assert.True(api.Exists);
        }
        finally
        {
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void Describe_without_data_dir_still_lists_tofu_and_default_oci()
    {
        var rows = ProgramPaths.Describe(null, ociConfigFile: null);
        Assert.Equal("", rows.Single(r => r.Id == "data").Path);
        Assert.False(rows.Single(r => r.Id == "data").Exists);
        Assert.Equal("", rows.Single(r => r.Id == "config").Path);

        var oci = rows.Single(r => r.Id == "oci");
        Assert.EndsWith(Path.Combine(".oci", "config"), oci.Path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveOciConfigPath_expands_userprofile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resolved = ProgramPaths.ResolveOciConfigPath(@"%USERPROFILE%\.oci\config");
        Assert.Equal(Path.Combine(home, ".oci", "config"), resolved);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-programpaths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
