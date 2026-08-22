using System.Text.Json.Serialization;

namespace McManager.Core.Setup;

/// <summary>
/// Product sidecar and validation for derived unstructured / jar-root packs (Step 8.8 P9).
/// </summary>
public static class DerivedPackIdentity
{
    public const string SidecarEntryName = "mcmgr-pack.json";
    public const int SchemaVersion = 1;
    public const string SourceTag = "mcmgr-derived";

    public const int JavaMajorMin = 8;
    public const int JavaMajorMax = 25;

    public const string IdentityHelp =
        "Jar-only zips often guess these wrong. Correct them before install.";

    public const string MinecraftVersionLabel = "Minecraft version";
    public const string LoaderLabel = "Loader";
    public const string LoaderVersionLabel = "Loader version";
    public const string JavaMajorLabel = "Required Java";

    public const string IdentityIncompleteReason =
        "Confirm Minecraft version, loader, loader version, and Java first.";

    public static bool NeedsIdentityConfirm(ManualServerPackKind kind) =>
        kind == ManualServerPackKind.UnstructuredServer;

    public static bool TryNormalizeMinecraft(string? value, out string minecraft)
    {
        minecraft = (value ?? "").Trim();
        if (minecraft.Length == 0)
            return false;
        if (string.Equals(minecraft, "(unknown)", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    public static bool TryNormalizeLoader(string? value, out string loader)
    {
        loader = (value ?? "").Trim().ToLowerInvariant();
        return loader is MrpackAnalyzer.LoaderFabric
            or MrpackAnalyzer.LoaderForge
            or MrpackAnalyzer.LoaderNeoForge;
    }

    public static bool TryNormalizeLoaderVersion(string? value, out string loaderVersion)
    {
        loaderVersion = (value ?? "").Trim();
        return loaderVersion.Length > 0;
    }

    public static bool TryNormalizeJavaMajor(string? value, out int javaMajor)
    {
        javaMajor = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!int.TryParse(value.Trim(), out javaMajor))
            return false;
        return javaMajor is >= JavaMajorMin and <= JavaMajorMax;
    }

    public static int? JavaMajorForMinecraftOrNull(string? minecraftVersion) =>
        TryNormalizeMinecraft(minecraftVersion, out var mc) && MinecraftJavaFloor.TryGet(mc, out var j)
            ? j
            : null;

    public static bool IsComplete(
        string? minecraftVersion,
        string? loader,
        string? loaderVersion,
        string? javaMajorText) =>
        TryNormalizeMinecraft(minecraftVersion, out _)
        && TryNormalizeLoader(loader, out _)
        && TryNormalizeLoaderVersion(loaderVersion, out _)
        && TryNormalizeJavaMajor(javaMajorText, out _);

    public static bool IsComplete(
        string? minecraftVersion,
        string? loader,
        string? loaderVersion,
        int? javaMajor) =>
        TryNormalizeMinecraft(minecraftVersion, out _)
        && TryNormalizeLoader(loader, out _)
        && TryNormalizeLoaderVersion(loaderVersion, out _)
        && javaMajor is >= JavaMajorMin and <= JavaMajorMax;

    public static bool DisagreesWithDetection(
        string? detectedMinecraft,
        string? detectedLoader,
        string? confirmedMinecraft,
        string? confirmedLoader)
    {
        if (!TryNormalizeMinecraft(confirmedMinecraft, out var userMc)
            || !TryNormalizeLoader(confirmedLoader, out var userLoader))
            return false;

        var detMc = NormalizeDetectedToken(detectedMinecraft);
        var detLoader = NormalizeDetectedToken(detectedLoader);
        if (detMc.Length == 0 && detLoader.Length == 0)
            return false;

        var mcDiffers = detMc.Length > 0
            && !string.Equals(detMc, userMc, StringComparison.OrdinalIgnoreCase);
        var loaderDiffers = detLoader.Length > 0
            && !string.Equals(detLoader, userLoader, StringComparison.OrdinalIgnoreCase);
        return mcDiffers || loaderDiffers;
    }

    public static string FormatDetectionMismatchWarning(
        string? detectedMinecraft,
        string? detectedLoader,
        string? confirmedMinecraft,
        string? confirmedLoader)
    {
        var detMc = NormalizeDetectedToken(detectedMinecraft);
        var detLoader = NormalizeDetectedToken(detectedLoader);
        var detLoaderLabel = SetupPackImport.DisplayLoader(
            detLoader.Length > 0 ? detLoader : "unknown");
        var userLoaderLabel = SetupPackImport.DisplayLoader(confirmedLoader);
        var detMcDisplay = detMc.Length > 0 ? detMc : "(unknown)";
        var userMc = (confirmedMinecraft ?? "").Trim();
        return $"This zip looks like {detLoaderLabel} {detMcDisplay}. "
            + $"You entered {userLoaderLabel} {userMc}. Continue if you are correcting a bad guess.";
    }

    public static string DependencyKey(string loader) =>
        (loader ?? "").Trim().ToLowerInvariant() switch
        {
            MrpackAnalyzer.LoaderFabric => "fabric-loader",
            MrpackAnalyzer.LoaderNeoForge => "neoforge",
            MrpackAnalyzer.LoaderForge => "forge",
            _ => "forge",
        };

    private static string NormalizeDetectedToken(string? value)
    {
        var t = (value ?? "").Trim();
        if (t.Length == 0 || string.Equals(t, "unknown", StringComparison.OrdinalIgnoreCase))
            return "";
        if (string.Equals(t, "(unknown)", StringComparison.OrdinalIgnoreCase))
            return "";
        return t;
    }
}

/// <summary>Confirmed identity fields from the Setup / Change pack UI.</summary>
public sealed record DerivedPackFields(
    string MinecraftVersion,
    string Loader,
    string LoaderVersion,
    int JavaMajor);

/// <summary>JSON sidecar inside a derived pack zip.</summary>
public sealed class DerivedPackSidecar
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = DerivedPackIdentity.SchemaVersion;

    [JsonPropertyName("source")]
    public string Source { get; set; } = DerivedPackIdentity.SourceTag;

    [JsonPropertyName("packName")]
    public string PackName { get; set; } = "";

    [JsonPropertyName("minecraftVersion")]
    public string MinecraftVersion { get; set; } = "";

    [JsonPropertyName("loader")]
    public string Loader { get; set; } = "";

    [JsonPropertyName("loaderVersion")]
    public string LoaderVersion { get; set; } = "";

    [JsonPropertyName("javaMajor")]
    public int JavaMajor { get; set; }

    [JsonPropertyName("detectedMinecraftVersion")]
    public string? DetectedMinecraftVersion { get; set; }

    [JsonPropertyName("detectedLoader")]
    public string? DetectedLoader { get; set; }

    [JsonPropertyName("originalFileName")]
    public string? OriginalFileName { get; set; }

    public DerivedPackFields ToFields() =>
        new(MinecraftVersion, Loader, LoaderVersion, JavaMajor);
}
