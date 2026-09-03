using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Forge version client (blueprint §20). Source is <c>promotions_slim.json</c>,
/// not the ad-supported HTML download page. Installer jars have no published checksum.
/// This is the loader for packs that <em>declare</em> Forge (1.12.2-era, 1.20.1).
/// It is not a Setup radio next to NeoForge.
/// </summary>
public sealed class ForgePromotionsClient
{
    public const string PromotionsUrl =
        "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public const string MavenBase = "https://maven.minecraftforge.net/net/minecraftforge/forge";
    public const string UserAgent = "MCSTool/0.1 (https://github.com/maattox/MCSTool)";
    public const string HashAlgorithm = "none_published";
    public const string LoaderId = "forge";
    public const string ArtifactKindArgfile = "argfile_tree";
    public const string ArtifactKindJar = "single_jar";
    public const int HttpTimeoutSeconds = 45;
    public const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex ForgeVersionRe = new(
        @"^\d+(\.\d+)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient _http;

    public ForgePromotionsClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        EnsureUserAgent(_http);
    }

    public static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public static string CombinedVersion(string minecraftVersion, string forgeVersion) =>
        $"{minecraftVersion.Trim()}-{forgeVersion.Trim()}";

    public static string InstallerUrl(string minecraftVersion, string forgeVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(forgeVersion);
        var token = Encode(CombinedVersion(minecraftVersion, forgeVersion));
        var file = Encode($"forge-{CombinedVersion(minecraftVersion, forgeVersion)}-installer.jar");
        return $"{MavenBase}/{token}/{file}";
    }

    public static string InstallerFilename(string minecraftVersion, string forgeVersion) =>
        $"forge-{CombinedVersion(minecraftVersion, forgeVersion)}-installer.jar";

    public static string RunnableJarFilename(string minecraftVersion, string forgeVersion) =>
        $"forge-{CombinedVersion(minecraftVersion, forgeVersion)}.jar";

    public static string UnixArgsPath(string minecraftVersion, string forgeVersion) =>
        $"libraries/net/minecraftforge/forge/{CombinedVersion(minecraftVersion, forgeVersion)}/unix_args.txt";

    public async Task<ServiceResult<IReadOnlyDictionary<string, string>>> GetPromosAsync(
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(PromotionsUrl, cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<IReadOnlyDictionary<string, string>>.Fail(text.Error!);
        var promos = ParsePromos(text.Value!);
        return promos is null
            ? ServiceResult<IReadOnlyDictionary<string, string>>.Fail(
                "Unexpected format of Forge promotions_slim.json (missing promos map).")
            : ServiceResult<IReadOnlyDictionary<string, string>>.Ok(promos);
    }

    public async Task<ServiceResult<ForgeResolvedInstaller>> ResolveInstallerAsync(
        string minecraftVersion,
        string? forgeVersion = null,
        CancellationToken cancellationToken = default)
    {
        var promos = await GetPromosAsync(cancellationToken).ConfigureAwait(false);
        if (!promos.Succeeded)
            return ServiceResult<ForgeResolvedInstaller>.Fail(promos.Error!);
        return Resolve(minecraftVersion, promos.Value!, forgeVersion);
    }

    public static IReadOnlyDictionary<string, string>? ParsePromos(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<ForgePromosDocument>(json, JsonOptions);
            if (doc?.Promos is null || doc.Promos.Count == 0)
                return null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in doc.Promos)
            {
                var key = (kv.Key ?? "").Trim();
                var val = (kv.Value ?? "").Trim();
                if (key.Length == 0 || val.Length == 0)
                    continue;
                map[key] = val;
            }

            return map.Count == 0 ? null : map;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ServiceResult<ForgeResolvedInstaller> Resolve(
        string minecraftVersion,
        IReadOnlyDictionary<string, string> promos,
        string? forgeVersion = null)
    {
        var mc = (minecraftVersion ?? "").Trim();
        if (string.IsNullOrWhiteSpace(mc))
            return ServiceResult<ForgeResolvedInstaller>.Fail("Minecraft version is required.");

        if (!IsSupportedMinecraft(mc))
        {
            return ServiceResult<ForgeResolvedInstaller>.Fail(
                $"Forge is not supported for Minecraft {mc}. "
                + "The product floor is Minecraft 1.7 (1.12.2-era and 1.20.1 packs).");
        }

        var pin = string.IsNullOrWhiteSpace(forgeVersion) ? null : forgeVersion.Trim();
        string chosen;
        string promoUsed;
        if (pin is null)
        {
            if (TryPromo(promos, mc, "recommended", out var rec))
            {
                chosen = rec;
                promoUsed = "recommended";
            }
            else if (TryPromo(promos, mc, "latest", out var latest))
            {
                chosen = latest;
                promoUsed = "latest";
            }
            else
            {
                return ServiceResult<ForgeResolvedInstaller>.Fail(
                    $"No Forge recommended or latest promo is published for Minecraft {mc}.");
            }
        }
        else
        {
            if (!IsForgeVersionId(pin))
            {
                return ServiceResult<ForgeResolvedInstaller>.Fail(
                    $"Forge version {pin} is not a valid Forge version id.");
            }

            chosen = pin;
            promoUsed = "pinned";
        }

        var url = InstallerUrl(mc, chosen);
        if (!url.StartsWith(MavenBase, StringComparison.Ordinal)
            || !url.Contains("/forge-", StringComparison.Ordinal)
            || !url.EndsWith("-installer.jar", StringComparison.Ordinal)
            || url.Contains("files.minecraftforge.net", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<ForgeResolvedInstaller>.Fail(
                "Refusing Forge installer URL that is not maven.minecraftforge.net installer.jar.");
        }

        var argfile = UsesArgfileTree(mc);
        return ServiceResult<ForgeResolvedInstaller>.Ok(new ForgeResolvedInstaller(
            minecraftVersion: mc,
            loaderVersion: chosen,
            installerFilename: InstallerFilename(mc, chosen),
            installerDownloadUrl: url,
            runnableJarFilename: RunnableJarFilename(mc, chosen),
            unixArgsPath: argfile ? UnixArgsPath(mc, chosen) : null,
            artifactKind: argfile ? ArtifactKindArgfile : ArtifactKindJar,
            javaMajor: JavaMajorForMinecraft(mc),
            promoUsed: promoUsed));
    }

    /// <summary>Minecraft 1.7 is the product floor (§20.4). 1.6.4 and older refuse.</summary>
    public static bool IsSupportedMinecraft(string minecraftVersion)
    {
        if (!TryParseMinecraft(minecraftVersion, out var major, out var minor, out _))
            return false;
        if (major >= 26)
            return true;
        if (major != 1)
            return false;
        return minor >= 7;
    }

    /// <summary>
    /// 1.16.5 and earlier = single jar; 1.17+ = argfile tree (§20.3).
    /// </summary>
    public static bool UsesArgfileTree(string minecraftVersion)
    {
        if (!TryParseMinecraft(minecraftVersion, out var major, out var minor, out _))
            return false;
        if (major >= 26)
            return true;
        return major == 1 && minor >= 17;
    }

    /// <summary>
    /// Static Minecraft Java floor (§9.1 / §20.4). Forge below 1.18 needs Java 8
    /// except the 1.17 band (Java 16).
    /// </summary>
    public static int JavaMajorForMinecraft(string minecraftVersion)
    {
        var id = minecraftVersion.Trim();
        if (id.StartsWith("26.", StringComparison.OrdinalIgnoreCase)
            || StartsWithMc(id, "26"))
            return 25;
        if (StartsWithMc(id, "1.21") || StartsWithMc(id, "1.22")
            || StartsWithMc(id, "1.20.5") || StartsWithMc(id, "1.20.6"))
            return 21;
        if (StartsWithMc(id, "1.18") || StartsWithMc(id, "1.19") || StartsWithMc(id, "1.20"))
            return 17;
        if (StartsWithMc(id, "1.17"))
            return 16;
        return 8;
    }

    public static bool IsForgeVersionId(string raw) =>
        ForgeVersionRe.IsMatch((raw ?? "").Trim());

    public static bool TryParseMinecraft(string minecraftVersion, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;
        var id = (minecraftVersion ?? "").Trim();
        var parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
            return false;
        if (!int.TryParse(parts[0], out major) || !int.TryParse(parts[1], out minor))
            return false;
        if (parts.Length == 3)
        {
            var patchToken = parts[2];
            var dash = patchToken.IndexOf('-');
            if (dash >= 0)
                patchToken = patchToken[..dash];
            if (!int.TryParse(patchToken, out patch))
                return false;
        }

        return major > 0 && minor >= 0 && patch >= 0;
    }

    private static bool TryPromo(
        IReadOnlyDictionary<string, string> promos,
        string mc,
        string channel,
        out string version)
    {
        var key = $"{mc}-{channel}";
        if (promos.TryGetValue(key, out var raw))
        {
            var v = (raw ?? "").Trim();
            if (v.Length > 0 && IsForgeVersionId(v))
            {
                version = v;
                return true;
            }
        }

        version = "";
        return false;
    }

    private static bool StartsWithMc(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return id.Length == prefix.Length || !char.IsDigit(id[prefix.Length]);
    }

    private static string Encode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Uri.EscapeDataString(value.Trim());
    }

    private async Task<ServiceResult<string>> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return ServiceResult<string>.Ok(body);

                var code = (int)response.StatusCode;
                if (code >= 500 && attempt < MaxAttempts)
                    continue;
                return ServiceResult<string>.Fail(
                    $"could not reach files.minecraftforge.net (HTTP {code} from {url}).");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                last = new TimeoutException("HTTP timeout");
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }

            if (attempt >= MaxAttempts)
                break;
        }

        var detail = last is null ? "network timeout or connection failure" : last.Message;
        return ServiceResult<string>.Fail(
            $"could not reach files.minecraftforge.net ({detail}).");
    }

    private sealed class ForgePromosDocument
    {
        [JsonPropertyName("promos")]
        public Dictionary<string, string>? Promos { get; set; }
    }
}

public sealed class ForgeResolvedInstaller
{
    public ForgeResolvedInstaller(
        string minecraftVersion,
        string loaderVersion,
        string installerFilename,
        string installerDownloadUrl,
        string runnableJarFilename,
        string? unixArgsPath,
        string artifactKind,
        int javaMajor,
        string promoUsed)
    {
        MinecraftVersion = minecraftVersion;
        LoaderVersion = loaderVersion;
        InstallerFilename = installerFilename;
        InstallerDownloadUrl = installerDownloadUrl;
        RunnableJarFilename = runnableJarFilename;
        UnixArgsPath = unixArgsPath;
        ArtifactKind = artifactKind;
        JavaMajor = javaMajor;
        PromoUsed = promoUsed;
    }

    public string MinecraftVersion { get; }
    public string LoaderVersion { get; }
    public string InstallerFilename { get; }
    public string InstallerDownloadUrl { get; }
    public string RunnableJarFilename { get; }
    public string? UnixArgsPath { get; }
    public string ArtifactKind { get; }
    public int JavaMajor { get; }
    public string PromoUsed { get; }
    public string HashAlgorithm => ForgePromotionsClient.HashAlgorithm;
    public string Loader => ForgePromotionsClient.LoaderId;
}
