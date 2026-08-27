using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class Layer2LocalOverlayTests
{
    [Fact]
    public void Promote_exclude_is_per_archive_hash_not_global()
    {
        var data = Path.Combine(Path.GetTempPath(), "mcmgr-l2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            var pack = Path.Combine(data, "pack.mrpack");
            File.WriteAllBytes(pack, [1, 2, 3, 9]);
            var hash = Layer2LocalOverlay.TryHashFile(pack);
            Assert.False(string.IsNullOrWhiteSpace(hash));

            Layer2LocalOverlay.PromoteExclude(data, hash!, "badmod");
            var matcher = ExcludeIncludeMatcher.ForModrinth(data, hash);
            Assert.Equal(
                ExcludeIncludeDecision.Exclude,
                matcher.Match("unrelated-slug", "mods/badmod-1.2.3.jar").Decision);

            var other = ExcludeIncludeMatcher.ForModrinth(data, "deadbeef");
            Assert.Equal(
                ExcludeIncludeDecision.NoMatch,
                other.Match("unrelated-slug", "mods/badmod-1.2.3.jar").Decision);

            var lists = Layer2LocalOverlay.Load(data);
            Assert.DoesNotContain(lists.GlobalExcludes, s => s.Equals("badmod", StringComparison.OrdinalIgnoreCase));
            Assert.True(lists.TryGetPack(Layer2LocalOverlay.IdentityKey(hash!), out var packEntry));
            Assert.Contains(packEntry.Excludes, s => s.Equals("badmod", StringComparison.OrdinalIgnoreCase));

            Layer2LocalOverlay.PromoteForceInclude(data, hash!, "badmod");
            lists = Layer2LocalOverlay.Load(data);
            Assert.True(lists.TryGetPack(Layer2LocalOverlay.IdentityKey(hash!), out packEntry));
            Assert.Contains(packEntry.ForceIncludes, s => s.Equals("badmod", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(packEntry.Excludes, s => s.Equals("badmod", StringComparison.OrdinalIgnoreCase));
            matcher = ExcludeIncludeMatcher.ForModrinth(data, hash);
            Assert.Equal(
                ExcludeIncludeDecision.Keep,
                matcher.Match("unrelated-slug", "mods/badmod-1.2.3.jar").Decision);
        }
        finally
        {
            try
            {
                Directory.Delete(data, recursive: true);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
