using McManager.Core.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ServerIconComposerTests
{
    [Fact]
    public void Contain_fit_pads_non_square_source()
    {
        var tall = SolidPng(24, 48, 20, 180, 40);
        var set = ServerIconComposer.Compose(tall);
        Assert.True(set.Succeeded, set.Error);
        AssertIcon(set.Value!.ColorPng);
        Assert.True(HasDirtPad(set.Value.ColorPng));
    }

    [Fact]
    public void Overlays_change_pixels_versus_greyscale_only()
    {
        var set = ServerIconComposer.Compose();
        Assert.True(set.Succeeded, set.Error);
        using var idle = Image.Load<Rgba32>(set.Value!.IdlePng);
        using var starting = Image.Load<Rgba32>(set.Value.StartingPng);
        using var exhausted = Image.Load<Rgba32>(set.Value.ExhaustedPng);
        Assert.True(CountNonGrey(starting) > 0);
        Assert.True(CountNonGrey(exhausted) > 0);
        Assert.True(PixelDiff(idle, starting) > 80);
        Assert.True(PixelDiff(idle, exhausted) > 80);
    }

    [Fact]
    public void Writes_door_vm_greenfield_defaults()
    {
        var set = ServerIconComposer.Compose();
        Assert.True(set.Succeeded, set.Error);
        var dir = FindDoorIconsDir();
        Assert.False(string.IsNullOrEmpty(dir), "door_vm/assets/icons not found");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "idle.png"), set.Value!.IdlePng);
        File.WriteAllBytes(Path.Combine(dir, "starting.png"), set.Value.StartingPng);
        File.WriteAllBytes(Path.Combine(dir, "exhausted.png"), set.Value.ExhaustedPng);
        Assert.True(File.Exists(Path.Combine(dir, "idle.png")));
    }

    [Fact]
    public void Examples_are_64_reference_only()
    {
        var root = FindServerIconsDir();
        Assert.False(string.IsNullOrEmpty(root), "assets/server-icons not found");
        foreach (var name in new[]
                 {
                     "example-offline.png",
                     "example-starting.png",
                     "example-unavailable.png",
                     "example-user-input.png",
                 })
        {
            var path = Path.Combine(root, name);
            Assert.True(File.Exists(path), path);
            Assert.True(ServerIdentityUx.TryReadPngSize(File.ReadAllBytes(path), out var w, out var h));
            Assert.Equal(64, w);
            Assert.Equal(64, h);
        }
    }

    public static byte[] SolidPng(int width, int height, byte r, byte g, byte b)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(r, g, b, 255));
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static void AssertIcon(byte[] png)
    {
        Assert.Null(ServerIdentityUx.ValidateIcon(png));
        Assert.True(ServerIdentityUx.TryReadPngSize(png, out var w, out var h));
        Assert.Equal(64, w);
        Assert.Equal(64, h);
    }

    private static bool HasDirtPad(byte[] png)
    {
        using var image = Image.Load<Rgba32>(png);
        var dirt = 0;
        image.ProcessPixelRows(accessor =>
        {
            var row = accessor.GetRowSpan(0);
            for (var x = 0; x < row.Length; x++)
            {
                var p = row[x];
                if (Math.Abs(p.R - ServerIconComposer.DirtPad.R) < 8
                    && Math.Abs(p.G - ServerIconComposer.DirtPad.G) < 8
                    && Math.Abs(p.B - ServerIconComposer.DirtPad.B) < 8)
                {
                    dirt++;
                }
            }
        });
        return dirt > 8;
    }

    private static int CountNonGrey(Image<Rgba32> image)
    {
        var n = 0;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    var max = Math.Max(p.R, Math.Max(p.G, p.B));
                    var min = Math.Min(p.R, Math.Min(p.G, p.B));
                    if (max - min > 18)
                        n++;
                }
            }
        });
        return n;
    }

    private static int PixelDiff(Image<Rgba32> a, Image<Rgba32> b)
    {
        var n = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var pa = a[x, y];
                var pb = b[x, y];
                if (pa.R != pb.R || pa.G != pb.G || pa.B != pb.B)
                    n++;
            }
        }

        return n;
    }

    private static string? FindDoorIconsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var door = Path.Combine(dir.FullName, "door_vm");
            if (File.Exists(Path.Combine(door, "Makefile")))
                return Path.Combine(door, "assets", "icons");

            dir = dir.Parent;
        }

        return null;
    }

    private static string? FindServerIconsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "server-icons");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
