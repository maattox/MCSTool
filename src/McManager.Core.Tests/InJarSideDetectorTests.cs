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
    public void Forge_displayTest_is_not_a_side_signal()
    {
        var peek = PeekJar(("META-INF/neoforge.mods.toml", """
            [[mods]]
            modId="insanelib"
            displayTest="IGNORE_SERVER_VERSION"
            """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
        Assert.Equal(MrpackAnalyzer.LoaderNeoForge, peek.Loader);
    }

    [Fact]
    public void Common_mixin_class_with_onlyin_client_is_client_even_with_forge_toml()
    {
        var peek = PeekJar(
            ("META-INF/mods.toml", (object)"""
                [[mods]]
                modId="holdmyitems"
                [[dependencies.holdmyitems]]
                modId="minecraft"
                side="BOTH"
                """),
            ("holdmyitems.mixins.json", """
                {"package":"com.example.mixin","mixins":["LivingEntityMixin"],"client":[]}
                """),
            ("holdmyitems.refmap.json", """
                {"mappings":{"com/example/mixin/LivingEntityMixin":{"hurt":"Lnet/minecraft/world/entity/LivingEntity;hurt()Z"}}}
                """),
            ("com/example/mixin/LivingEntityMixin.class", MakeOnlyInClientClass("com/example/mixin/LivingEntityMixin")));
        Assert.True(peek.HadMetadata);
        Assert.Equal("client", peek.Environment);
    }

    [Fact]
    public void Client_gated_onlyin_mixin_is_not_stripped()
    {
        var peek = PeekJar(
            ("META-INF/mods.toml", (object)"""
                [[mods]]
                modId="library"
                """),
            ("example.mixins.json", """
                {"package":"com.example.mixin","mixins":["EntityMixin"],"client":["GuiMixin"]}
                """),
            ("com/example/mixin/EntityMixin.class", MakePlainClass("com/example/mixin/EntityMixin")),
            ("com/example/mixin/GuiMixin.class", MakeOnlyInClientClass("com/example/mixin/GuiMixin")));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
    }

    [Fact]
    public void Forge_toml_without_client_marker_is_kept_despite_one_client_common_mixin()
    {
        var peek = PeekJar(
            ("META-INF/mods.toml", """
                [[mods]]
                modId="cofh_core"
                [[dependencies.cofh_core]]
                modId="minecraft"
                side="BOTH"
                """),
            ("mixins.cofhcore.json", """
                {"package":"cofh.core.mixin","client":["GameRendererMixin"],"mixins":["LivingEntityMixin","MultiPlayerGameModeMixin"]}
                """),
            ("mixins.cofhcore.refmap.json", """
                {"mappings":{
                  "cofh/core/mixin/LivingEntityMixin":{"hurt":"Lnet/minecraft/world/entity/LivingEntity;hurt()Z"},
                  "cofh/core/mixin/MultiPlayerGameModeMixin":{"sameDestroyTarget":"Lnet/minecraft/client/multiplayer/MultiPlayerGameMode;sameDestroyTarget()Z"}
                }}
                """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
        Assert.Equal(MrpackAnalyzer.LoaderForge, peek.Loader);
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
    public void Common_mixins_with_world_and_client_targets_are_kept()
    {
        var peek = PeekJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":["HeldItemMixin","LivingEntityMixin"],"client":[]}"""),
            ("example.refmap.json", """
                {"mappings":{
                  "com/example/mixin/HeldItemMixin":{"net/minecraft/client/renderer/ItemInHandRenderer":"Lnet/minecraft/client/renderer/ItemInHandRenderer;"},
                  "com/example/mixin/LivingEntityMixin":{"hurt":"Lnet/minecraft/world/entity/LivingEntity;hurt()Z"}
                }}
                """));
        Assert.False(peek.HadMetadata);
        Assert.Equal("*", peek.Environment);
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
        Assert.True(InJarSideDetector.LooksLikeDedicatedSafeClass("net.minecraft.world.entity.Entity"));
        Assert.True(InJarSideDetector.LooksLikeDedicatedSafeClass("Lnet/minecraft/world/entity/LivingEntity;hurt()Z"));
        Assert.False(InJarSideDetector.LooksLikeDedicatedSafeClass("net.minecraft.client.Minecraft"));
    }

    [Fact]
    public void Class_file_parser_reads_onlyin_client_annotation()
    {
        Assert.True(InJarSideDetector.ClassFileHasClientDistAnnotation(
            MakeOnlyInClientClass("com/example/mixin/LivingEntityMixin")));
        Assert.False(InJarSideDetector.ClassFileHasClientDistAnnotation(
            MakePlainClass("com/example/mixin/LivingEntityMixin")));
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
        using var zip = MakeZip(entries.Select(e => (e.Name, (object)e.Content)).ToArray());
        return InJarSideDetector.Peek(zip);
    }

    private static InJarSideDetector.PeekResult PeekJar(params (string Name, object Content)[] entries)
    {
        using var zip = MakeZip(entries);
        return InJarSideDetector.Peek(zip);
    }

    private static MemoryStream MakeZip(params (string Name, object Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var output = entry.Open();
                var bytes = content as byte[] ?? Encoding.UTF8.GetBytes((string)content);
                output.Write(bytes);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static byte[] MakePlainClass(string thisClass) =>
        WriteClassFile(thisClass, withOnlyInClient: false);

    private static byte[] MakeOnlyInClientClass(string thisClass) =>
        WriteClassFile(thisClass, withOnlyInClient: true);

    private static byte[] WriteClassFile(string thisClass, bool withOnlyInClient)
    {
        var utf8 = new List<string>
        {
            "java/lang/Object",
            thisClass,
        };
        if (withOnlyInClient)
        {
            utf8.Add("RuntimeVisibleAnnotations");
            utf8.Add("Lnet/minecraftforge/api/distmarker/OnlyIn;");
            utf8.Add("value");
            utf8.Add("Lnet/minecraftforge/api/distmarker/Dist;");
            utf8.Add("CLIENT");
        }

        using var ms = new MemoryStream();
        void U1(int v) => ms.WriteByte((byte)v);
        void U2(int v)
        {
            U1(v >> 8);
            U1(v);
        }

        void U4(int v)
        {
            U1(v >> 24);
            U1(v >> 16);
            U1(v >> 8);
            U1(v);
        }

        void Utf8(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            U1(1);
            U2(bytes.Length);
            ms.Write(bytes);
        }

        ms.Write([0xCA, 0xFE, 0xBA, 0xBE]);
        U2(0);
        U2(52);
        U2(utf8.Count + 3);
        Utf8(utf8[0]);
        U1(7);
        U2(1);
        Utf8(utf8[1]);
        U1(7);
        U2(3);
        for (var i = 2; i < utf8.Count; i++)
            Utf8(utf8[i]);

        U2(0x0021);
        U2(4);
        U2(2);
        U2(0);
        U2(0);
        U2(0);
        if (!withOnlyInClient)
        {
            U2(0);
            return ms.ToArray();
        }

        U2(1);
        U2(5);
        U4(13);
        U2(1);
        U2(6);
        U2(1);
        U2(7);
        U1((byte)'e');
        U2(8);
        U2(9);
        return ms.ToArray();
    }
}
