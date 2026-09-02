using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ModdingPanelLogicTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("vanilla")]
    [InlineData("Vanilla")]
    [InlineData("paper")]
    [InlineData("unspecified")]
    public void Vanilla_and_paper_are_not_modded(string? kind)
    {
        Assert.False(ModdingPanelLogic.IsModdedServerKind(kind));
        Assert.Equal(ModdingPanelLogic.VanillaEmptyState, ModdingPanelLogic.DownloadDisabledReason(false, true));
        Assert.False(ModdingPanelLogic.CanDownloadPack(isModded: false, hasLocalArchive: true));
    }

    [Fact]
    public void Paper_is_not_modded_and_has_plugin_copy()
    {
        Assert.True(ModdingPanelLogic.IsPaperServerKind("paper"));
        Assert.True(ModdingPanelLogic.IsPaperServerKind("Paper"));
        Assert.False(ModdingPanelLogic.IsPaperServerKind("vanilla"));
        Assert.False(ModdingPanelLogic.IsPaperServerKind("fabric"));
        Assert.Contains("Paper plugins only", ModdingPanelLogic.PaperEmptyState, StringComparison.Ordinal);
        Assert.Contains("/reload", ModdingPanelLogic.PaperHelpTitle, StringComparison.OrdinalIgnoreCase);
        Assert.True(ModdingPanelLogic.ShowPluginsTab(isPaperServer: true));
        Assert.False(ModdingPanelLogic.ShowPluginsTab(isPaperServer: false));
    }

    [Theory]
    [InlineData("pack", false, "modding")]
    [InlineData("pack", true, "modding")]
    [InlineData("plugins", false, "modding")]
    [InlineData("plugins", true, "plugins")]
    [InlineData("modding", true, "modding")]
    [InlineData("identity", false, "identity")]
    [InlineData("", true, "")]
    [InlineData(null, false, "")]
    public void Normalize_server_pane_hides_plugins_unless_paper(
        string? pane,
        bool isPaper,
        string expected)
    {
        Assert.Equal(expected, ModdingPanelLogic.NormalizeServerPane(pane, isPaper));
    }

    [Theory]
    [InlineData("modded")]
    [InlineData("fabric")]
    [InlineData("forge")]
    [InlineData("neoforge")]
    [InlineData("quilt")]
    [InlineData("Fabric")]
    public void Loader_kinds_are_modded(string kind)
    {
        Assert.True(ModdingPanelLogic.IsModdedServerKind(kind));
    }

    [Fact]
    public void Download_disabled_when_local_archive_is_missing()
    {
        Assert.False(ModdingPanelLogic.CanDownloadPack(isModded: true, hasLocalArchive: false));
        Assert.Equal(
            ModdingPanelLogic.MissingArchiveMessage,
            ModdingPanelLogic.DownloadDisabledReason(isModded: true, hasLocalArchive: false));
        Assert.Contains("cannot rebuild a client pack", ModdingPanelLogic.MissingArchiveMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("zip of", ModdingPanelLogic.MissingArchiveMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Download_enabled_when_modded_and_archive_present()
    {
        Assert.True(ModdingPanelLogic.CanDownloadPack(isModded: true, hasLocalArchive: true));
        Assert.Equal("", ModdingPanelLogic.DownloadDisabledReason(isModded: true, hasLocalArchive: true));
    }
}
