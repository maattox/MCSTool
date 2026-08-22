using System.Buffers.Binary;
using McManager.Core.Config;
using McManager.Core.Setup;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// MOTD-scale identity helpers (plain text + 64×64 PNG). Not a rich MOTD editor.
/// </summary>
public static class ServerIdentityUx
{
    public const int IconWidth = 64;
    public const int IconHeight = 64;
    public const int MaxIconBytes = 256 * 1024;
    public const int MaxNameLength = 40;
    public const int MaxDescriptionLength = 80;
    public const string DefaultMotd = "A Minecraft Server";
    public const string DefaultDescription = "made with github.com/maattox/oci-mc-server";
    public const string DefaultVanillaName = "Vanilla Server";
    public const string DefaultPaperName = "Paper Server";
    public const string DefaultModdedName = "Modded Server";

    public static IReadOnlyDictionary<string, string> DefaultChatMessages =>
        ChatMessagesDocument.DefaultChatMessages;

    public static readonly IReadOnlyList<ChatTemplateField> ChatTemplateFields =
    [
        new("budget_warn_leftover", "Daily leftover warning", "{ocpu} {gb}"),
        new("budget_final_warn", "Daily hours almost gone", null),
        new("budget_stop", "Daily hours used up", null),
        new("soft_cap_stop", "Monthly cap stop", null),
        new("idle_stop", "Idle (nobody online)", "{minutes}"),
        new("idle_stop_inactive", "Idle (Minecraft not running)", "{minutes}"),
        new("admin_stop", "Admin stop", null),
    ];

    /// <summary>
    /// Minecraft <c>motd</c> value: name, then description on a second line when both are set.
    /// Empty identity → the default MOTD. Real newlines become <c>\n</c>.
    /// </summary>
    public static string BuildMotd(string? serverName, string? description)
    {
        var name = CollapseWhitespace(serverName);
        var desc = NormalizeDescription(description);
        string text;
        if (name.Length > 0 && desc.Length > 0)
            text = name + "\\n" + desc;
        else if (desc.Length > 0)
            text = desc;
        else if (name.Length > 0)
            text = name;
        else
            text = DefaultMotd;
        return text;
    }

    public static string? ValidateIcon(byte[]? png)
    {
        if (png is null || png.Length == 0)
            return "Choose a PNG file.";
        if (png.Length > MaxIconBytes)
            return $"Icon is too large ({png.Length} bytes). Use a PNG that fits under 256 KB after 64×64.";
        if (!TryReadPngSize(png, out var width, out var height))
            return "Icon must be a PNG file.";
        if (width != IconWidth || height != IconHeight)
            return $"Minecraft needs a {IconWidth}×{IconHeight} PNG (this file is {width}×{height}).";
        return null;
    }

    /// <summary>User-picked source before contain-fit. Any reasonable PNG; size is not required to be 64×64.</summary>
    public static string? ValidateSourceIcon(byte[]? png) => ServerIconComposer.ValidateSourceIcon(png);

    public static bool TryReadPngSize(ReadOnlySpan<byte> png, out int width, out int height)
    {
        width = 0;
        height = 0;
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature))
            return false;
        width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return width > 0 && height > 0;
    }

    public static string DisplayName(string? customName, string? fallbackVmDisplayName)
    {
        var custom = CollapseWhitespace(customName);
        if (custom.Length > 0)
            return custom;
        var fallback = CollapseWhitespace(fallbackVmDisplayName);
        return fallback.Length > 0 ? fallback : "—";
    }

    /// <summary>
    /// Type-based Setup default. No Oracle trademark wording.
    /// Vanilla (Mojang) vs Paper vs Modded.
    /// </summary>
    public static string DefaultServerName(string? serverType, string? vanillaFlavor)
    {
        if (SetupServerType.IsModded(serverType))
            return DefaultModdedName;
        if (SetupVanillaFlavor.IsOptimized(vanillaFlavor))
            return DefaultPaperName;
        return DefaultVanillaName;
    }

    public static ChatMessagesDocument CreateSetupSeed(SetupWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var doc = ChatMessagesDocument.Defaults();
        var name = state.IdentityName?.Trim() ?? "";
        doc.ServerName = name.Length > 0
            ? name
            : DefaultServerName(state.ServerType, state.VanillaFlavor);
        var desc = state.IdentityDescription?.Trim() ?? "";
        doc.Description = desc.Length > 0 || state.IdentityDescriptionCustomized
            ? desc
            : DefaultDescription;
        return doc;
    }

    /// <summary>
    /// Read a local PNG for Setup seed. Returns null when missing (caller uses the product default).
    /// Invalid files still return null with <paramref name="skipReason"/>.
    /// </summary>
    public static byte[]? TryReadSetupIcon(string? path, out string? skipReason)
    {
        skipReason = null;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (!File.Exists(path))
        {
            skipReason = "Icon file is missing; using the default icon.";
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            skipReason = "Could not read the icon file: " + ex.Message;
            return null;
        }

        var error = ValidateSourceIcon(bytes);
        if (error is not null)
        {
            skipReason = error;
            return null;
        }

        return bytes;
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return string.Join("\\n", lines.Select(CollapseWhitespace).Where(s => s.Length > 0));
    }
}

public sealed record ChatTemplateField(string Key, string Label, string? Placeholders);
