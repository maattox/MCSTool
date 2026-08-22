using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace McManager.Core.Services;

/// <summary>
/// Admin-PC icon pipeline (P8): contain-fit to 64×64, Rec. 601 greyscale, overlay door states.
/// Processing never runs on VM1 or the door Micro.
/// </summary>
public static class ServerIconComposer
{
    public const int Size = ServerIdentityUx.IconWidth;
    public const int MaxSourceBytes = 2 * 1024 * 1024;
    public const int MaxSourceEdge = 2048;

    /// <summary>Minecraft dirt-block brown used when contain-fit letterboxes.</summary>
    public static readonly Rgba32 DirtPad = new(134, 96, 67, 255);

    public const string DefaultIconResource = "McManager.Core.ServerIcons.default-icon.png";
    public const string OverlayOfflineResource = "McManager.Core.ServerIcons.overlay-offline.png";
    public const string OverlayStartingResource = "McManager.Core.ServerIcons.overlay-starting.png";
    public const string OverlayUnavailableResource = "McManager.Core.ServerIcons.overlay-unavailable.png";

    private static readonly PngEncoder PngOut = new()
    {
        ColorType = PngColorType.RgbWithAlpha,
        CompressionLevel = PngCompressionLevel.BestCompression,
    };

    public static string? ValidateSourceIcon(byte[]? png)
    {
        if (png is null || png.Length == 0)
            return "Choose a PNG file.";
        if (png.Length > MaxSourceBytes)
            return $"Icon is too large ({png.Length} bytes). Use a PNG under 2 MB.";
        if (!ServerIdentityUx.TryReadPngSize(png, out var width, out var height))
            return "Icon must be a PNG file.";
        if (width > MaxSourceEdge || height > MaxSourceEdge)
            return $"Icon is too large ({width}×{height}). Use an image at most {MaxSourceEdge}×{MaxSourceEdge}.";
        return null;
    }

    /// <summary>
    /// Fit <paramref name="sourcePng"/> (or the product default) to 64×64 color plus three door variants.
    /// Unavailable and spend-brake share the exhausted art.
    /// </summary>
    public static ServiceResult<ServerIconSet> Compose(byte[]? sourcePng = null)
    {
        var source = sourcePng is { Length: > 0 } ? sourcePng : LoadResource(DefaultIconResource);
        if (source is null || source.Length == 0)
            return ServiceResult<ServerIconSet>.Fail("Default server icon is missing from this Manager build.");

        if (sourcePng is { Length: > 0 })
        {
            var sourceError = ValidateSourceIcon(sourcePng);
            if (sourceError is not null)
                return ServiceResult<ServerIconSet>.Fail(sourceError);
        }

        try
        {
            using var color = FitToSquare(source);
            using var grey = color.Clone();
            ToLuma601(grey);

            var colorPng = Encode(color);
            var idle = Overlay(grey, OverlayOfflineResource);
            var starting = Overlay(grey, OverlayStartingResource);
            var exhausted = Overlay(grey, OverlayUnavailableResource);
            return ServiceResult<ServerIconSet>.Ok(new ServerIconSet
            {
                ColorPng = colorPng,
                IdlePng = idle,
                StartingPng = starting,
                ExhaustedPng = exhausted,
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<ServerIconSet>.Fail("Could not process that icon: " + ex.Message);
        }
    }

    public static string ToDataUrl(byte[] png) =>
        "data:image/png;base64," + Convert.ToBase64String(png);

    public static byte[]? LoadResource(string logicalName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(logicalName);
        if (stream is null)
            return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Image<Rgba32> FitToSquare(byte[] png)
    {
        var image = Image.Load<Rgba32>(png);
        if (image.Width == Size && image.Height == Size)
            return image;

        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(Size, Size),
            Mode = ResizeMode.Pad,
            Position = AnchorPositionMode.Center,
            PadColor = DirtPad,
            Sampler = KnownResamplers.Lanczos3,
        }));
        return image;
    }

    private static void ToLuma601(Image<Rgba32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    var luma = (byte)Math.Clamp(
                        (0.299 * p.R) + (0.587 * p.G) + (0.114 * p.B),
                        0,
                        255);
                    row[x] = new Rgba32(luma, luma, luma, p.A);
                }
            }
        });
    }

    private static byte[] Overlay(Image<Rgba32> greyscale, string overlayResource)
    {
        var overlayBytes = LoadResource(overlayResource)
            ?? throw new InvalidOperationException("Missing overlay " + overlayResource);
        using var canvas = greyscale.Clone();
        using var overlay = Image.Load<Rgba32>(overlayBytes);
        if (overlay.Width != Size || overlay.Height != Size)
        {
            overlay.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(Size, Size),
                Mode = ResizeMode.Pad,
                Position = AnchorPositionMode.Center,
                PadColor = Color.Transparent,
                Sampler = KnownResamplers.NearestNeighbor,
            }));
        }

        canvas.Mutate(ctx => ctx.DrawImage(overlay, new Point(0, 0), 1f));
        return Encode(canvas);
    }

    private static byte[] Encode(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, PngOut);
        return ms.ToArray();
    }
}

public sealed class ServerIconSet
{
    public required byte[] ColorPng { get; init; }
    public required byte[] IdlePng { get; init; }
    public required byte[] StartingPng { get; init; }
    public required byte[] ExhaustedPng { get; init; }

    public string ColorDataUrl => ServerIconComposer.ToDataUrl(ColorPng);
    public string IdleDataUrl => ServerIconComposer.ToDataUrl(IdlePng);
    public string StartingDataUrl => ServerIconComposer.ToDataUrl(StartingPng);
    public string ExhaustedDataUrl => ServerIconComposer.ToDataUrl(ExhaustedPng);
}
