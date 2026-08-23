using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class CrashModAttributorTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "journals", name));

    [Fact]
    public void Forge_one_mod_loader_report_is_exactly_one()
    {
        var blame = CrashModAttributor.TryExactlyOne(Fixture("forge-mixin-invalid-dist.txt"));
        Assert.NotNull(blame);
        Assert.Equal("exampleclientmod", blame.ModId);
    }

    [Fact]
    public void Fabric_provided_by_one_mod_is_exactly_one()
    {
        var blame = CrashModAttributor.TryExactlyOne(Fixture("fabric-noclassdeffound-abort.txt"));
        Assert.NotNull(blame);
        Assert.Equal("exampleguimod", blame.ModId);
    }

    [Fact]
    public void Two_mods_in_loader_list_does_nothing()
    {
        Assert.Null(CrashModAttributor.TryExactlyOne(Fixture("forge-two-mod-crash.txt")));
    }

    [Fact]
    public void Mixin_only_without_loader_blame_does_nothing()
    {
        Assert.Null(CrashModAttributor.TryExactlyOne(Fixture("mixin-only-no-loader-blame.txt")));
    }

    [Fact]
    public void Empty_or_java_only_crash_does_nothing()
    {
        Assert.Null(CrashModAttributor.TryExactlyOne(Fixture("unsupported-class-version.txt")));
        Assert.Null(CrashModAttributor.TryExactlyOne(""));
        Assert.Null(CrashModAttributor.TryExactlyOne(null));
    }

    [Fact]
    public void Affected_mods_ignores_minecraft_and_loader()
    {
        var blame = CrashModAttributor.TryExactlyOne(Fixture("fabric-affected-one-mod.txt"));
        Assert.NotNull(blame);
        Assert.Equal("onlygui", blame.ModId);
    }

    [Fact]
    public void Disagreeing_list_and_provided_by_is_ambiguous()
    {
        var text =
            "The following mods caused the server to crash:\nmodone\n"
            + "Could not execute entrypoint stage 'main' due to errors, provided by 'modtwo'!\n";
        Assert.Null(CrashModAttributor.TryExactlyOne(text));
    }

    [Fact]
    public void Mod_file_line_supplies_jar_hint()
    {
        var text =
            "The following mods caused the server to crash:\nbadmod\n"
            + "Mod File: /opt/mcmgr/server/mods/badmod-1.2.3.jar\n";
        var blame = CrashModAttributor.TryExactlyOne(text);
        Assert.NotNull(blame);
        Assert.Equal("badmod", blame.ModId);
        Assert.Equal("badmod-1.2.3.jar", blame.JarFileName);
    }

    [Fact]
    public void Unique_jar_match_uses_hint_then_token()
    {
        var blame = new CrashModBlame("badmod", "badmod-1.2.3.jar");
        var names = new[] { "goodmod-1.0.jar", "badmod-1.2.3.jar", "other-2.jar" };
        Assert.Equal("badmod-1.2.3.jar", CrashModAttributor.TryFindUniqueJar(blame, names));

        var noHint = new CrashModBlame("badmod", null);
        Assert.Equal("badmod-1.2.3.jar", CrashModAttributor.TryFindUniqueJar(noHint, names));

        var ambiguous = new CrashModBlame("mod", null);
        Assert.Null(CrashModAttributor.TryFindUniqueJar(ambiguous, ["mod-a.jar", "mod-b.jar"]));
        Assert.Null(CrashModAttributor.TryFindUniqueJar(new CrashModBlame("missing", null), names));
    }
}
