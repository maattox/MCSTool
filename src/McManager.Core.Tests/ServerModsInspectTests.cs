using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ServerModsInspectTests
{
    [Fact]
    public void Remote_script_is_inspect_only_under_server_mods()
    {
        var script = ServerModsInspect.RemoteScript;
        var command = ServerModsInspect.RemoteCommand;
        Assert.Contains("/opt/mcmgr/server/mods", script, StringComparison.Ordinal);
        Assert.Contains("/etc/mcmgr/game-manifest.json", script, StringComparison.Ordinal);
        Assert.Contains("HOME=\"${HOME:-/home/ubuntu}\"", script, StringComparison.Ordinal);
        Assert.Contains("sudo bash -c", command, StringComparison.Ordinal);
        Assert.DoesNotContain("zip", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
        Assert.Contains("-maxdepth 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_manifest_and_mod_file_names()
    {
        var stdout =
            """
            ---MANIFEST---
            {"distribution":"modded","loader":"fabric","loader_version":"0.16.9","minecraft_version":"1.21.1"}
            ---MODS---
            lithium.jar
            fabric-api.jar
            MCMGR_MODS_OK
            """;

        Assert.True(ServerModsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.Equal("modded", result.Distribution);
        Assert.Equal("fabric", result.Loader);
        Assert.Equal("0.16.9", result.LoaderVersion);
        Assert.Equal("1.21.1", result.MinecraftVersion);
        Assert.False(result.ModsDirectoryMissing);
        Assert.Equal(["fabric-api.jar", "lithium.jar"], result.FileNames.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Contains("Fabric 0.16.9", result.SummaryLine(), StringComparison.Ordinal);
        Assert.Contains("2 files in mods/", result.SummaryLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_unacknowledged_quarantined_files_from_manifest()
    {
        var stdout =
            """
            ---MANIFEST---
            {"distribution":"modded","loader":"forge","modpack":{"quarantined_files":[
              {"path":"mods/badmod-1.jar","reason":"crash_attributed_by_loader_report",
               "detected_at":"2026-08-23T12:00:00Z","retry_succeeded":true,"operator_acknowledged":false}
            ]}}
            ---MODS---
            lithium.jar
            MCMGR_MODS_OK
            """;

        Assert.True(ServerModsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.Single(result.QuarantinedFiles);
        Assert.Equal("mods/badmod-1.jar", result.QuarantinedFiles[0].Path);
        Assert.True(result.QuarantinedFiles[0].NeedsAck);
    }

    [Fact]
    public void Missing_mods_dir_is_an_empty_listing()
    {
        var stdout =
            """
            ---MANIFEST---
            MCMGR_MANIFEST_MISSING
            ---MODS---
            MCMGR_MODS_MISSING
            """;

        Assert.True(ServerModsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.True(result.ModsDirectoryMissing);
        Assert.Empty(result.FileNames);
        Assert.Contains("no mods folder", result.SummaryLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void Skips_unsafe_file_names()
    {
        var stdout =
            """
            ---MANIFEST---
            MCMGR_MANIFEST_MISSING
            ---MODS---
            ok.jar
            ../escape.jar
            nested/path.jar
            MCMGR_MODS_OK
            """;

        Assert.True(ServerModsInspect.TryParse(stdout, out var result, out var error), error);
        Assert.Equal(["ok.jar"], result.FileNames);
        Assert.False(ServerModsInspect.IsSafeFileName("../x"));
        Assert.False(ServerModsInspect.IsSafeFileName("a/b"));
        Assert.True(ServerModsInspect.IsSafeFileName("sodium-extra-0.1.jar"));
    }

    [Fact]
    public void Rejects_unmarked_output()
    {
        Assert.False(ServerModsInspect.TryParse("just some ls output", out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
