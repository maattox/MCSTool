using System.IO.Compression;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class InJarSideDetectorTests
{
    [Fact]
    public void Fabric_environment_client_is_client()
    {
        var peek = PeekJar(("fabric.mod.json", """{"schemaVersion":1,"id":"ui","version":"0","environment":"client"}"""));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, peek.Loader);
    }

    [Fact]
    public void Fabric_client_entrypoints_only_is_client()
    {
        var peek = PeekJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"ui","version":"0","entrypoints":{"client":["com.example.ClientInit"]}}
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
    }

    [Fact]
    public void Fabric_explicit_both_with_client_entrypoints_is_kept()
    {
        var peek = PeekJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"both","version":"0","environment":"*","entrypoints":{"client":["c"]}}
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Fabric_main_and_client_entrypoints_without_environment_is_kept()
    {
        var peek = PeekJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"both","version":"0","entrypoints":{"client":["c"],"main":["m"]}}
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Forge_clientSideOnly_is_client()
    {
        var peek = PeekJar(("META-INF/mods.toml", """
            modLoader="javafml"
            [[mods]]
            modId="fancyui"
            clientSideOnly=true
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
        Assert.Equal(MrpackAnalyzer.LoaderForge, peek.Loader);
    }

    [Fact]
    public void Forge_displayTest_ignore_server_version_is_client()
    {
        var peek = PeekJar(("META-INF/neoforge.mods.toml", """
            [[mods]]
            modId="minimap"
            displayTest="IGNORE_SERVER_VERSION"
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
        Assert.Equal(MrpackAnalyzer.LoaderNeoForge, peek.Loader);
    }

    [Fact]
    public void Forge_server_side_is_kept_with_metadata()
    {
        var peek = PeekJar(("META-INF/mods.toml", """
            [[mods]]
            modId="apisupport"
            side="SERVER"
            """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Forge_displayTest_ignore_all_without_side_stays_unclear()
    {
        var peek = PeekJar(("META-INF/mods.toml", """
            [[mods]]
            modId="library"
            displayTest="IGNORE_ALL_VERSION"
            """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Common_mixin_targeting_client_class_is_client()
    {
        var peek = PeekJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":["HeldItemMixin"],"client":[]}"""),
            ("example.refmap.json", """
                {"mappings":{"com/example/mixin/HeldItemMixin":{"net/minecraft/client/renderer/ItemInHandRenderer":"Lnet/minecraft/client/renderer/ItemInHandRenderer;"}}}
                """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
    }

    [Fact]
    public void Client_gated_mixins_only_are_not_stripped()
    {
        var peek = PeekJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":[],"client":["GuiMixin"]}"""),
            ("example.refmap.json", """
                {"mappings":{"com/example/mixin/GuiMixin":{"net/minecraft/client/gui/screens/Screen":"Lnet/minecraft/client/gui/screens/Screen;"}}}
                """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Common_mixin_targeting_world_class_is_not_stripped()
    {
        var peek = PeekJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":["EntityMixin"]}"""),
            ("example.refmap.json", """
                {"mappings":{"com/example/mixin/EntityMixin":{"net/minecraft/world/entity/Entity":"Lnet/minecraft/world/entity/Entity;"}}}
                """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Dual_side_jar_with_client_array_and_common_world_mixin_is_kept()
    {
        var peek = PeekJar(
            ("META-INF/mods.toml", """
                [[mods]]
                modId="both"
                side="BOTH"
                """),
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":["EntityMixin"],"client":["GuiMixin"]}"""),
            ("example.refmap.json", """
                {"mappings":{
                  "com/example/mixin/EntityMixin":{"net/minecraft/world/entity/Entity":"Lnet/minecraft/world/entity/Entity;"},
                  "com/example/mixin/GuiMixin":{"net/minecraft/client/gui/screens/Screen":"Lnet/minecraft/client/gui/screens/Screen;"}
                }}
                """));
        Assert.True(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Client_class_prefix_does_not_match_clientcommands()
    {
        Assert.False(InJarSideDetector.LooksLikeClientClass("net.minecraft.clientcommands.ClientCommand"));
        Assert.True(InJarSideDetector.LooksLikeClientClass("net.minecraft.client.Minecraft"));
        Assert.True(InJarSideDetector.LooksLikeClientClass("Lnet/minecraft/client/gui/screens/Screen;"));
        Assert.False(InJarSideDetector.LooksLikeClientClass("net.minecraft.world.entity.Entity"));
    }

    [Fact]
    public void Mixin_json_alone_is_not_client()
    {
        var peek = PeekJar(("example.mixins.json", """{"package":"com.example.mixin","mixins":["WhateverMixin"],"client":[]}"""));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    private static InJarSideDetector.PeekResult PeekJar(params (string Name, string Content)[] entries)
    {
        using var zip = MakeZip(entries);
        return InJarSideDetector.Peek(zip);
    }

    private static MemoryStream MakeZip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var output = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(content);
                output.Write(bytes);
            }
        }

        ms.Position = 0;
        return ms;
    }
}
