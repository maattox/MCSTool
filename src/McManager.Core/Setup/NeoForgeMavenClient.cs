using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// NeoForge Maven metadata client (blueprint §19). The only version source is
/// <c>maven-metadata.xml</c> — not JSON. Installer jars have no published checksum.
/// </summary>
public sealed class NeoForgeMavenClient
{
    public const string MavenBase = "https://maven.neoforged.net/releases/net/neoforged/neoforge";
    public const string UserAgent = "McManager/0.1 (https://github.com/maattox/MCSTool)";
    public const string HashAlgorithm = "none_published";
    public const string ArtifactKind = "argfile_tree";
    public const string LoaderId = "neoforge";
    public const int HttpTimeoutSeconds = 45;
    public const int MaxAttempts = 3;

    private readonly HttpClient _http;

    public NeoForgeMavenClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        EnsureUserAgent(_http);
    }

    public static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public static string MetadataUrl() => $"{MavenBase}/maven-metadata.xml";

    public static string InstallerUrl(string neoForgeVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(neoForgeVersion);
        var v = Encode(neoForgeVersion);
        return $"{MavenBase}/{v}/neoforge-{v}-installer.jar";
    }

    public static string InstallerFilename(string neoForgeVersion) =>
        $"neoforge-{neoForgeVersion.Trim()}-installer.jar";

    public static string UnixArgsPath(string neoForgeVersion) =>
        $"libraries/net/neoforged/neoforge/{neoForgeVersion.Trim()}/unix_args.txt";

    public async Task<ServiceResult<IReadOnlyList<string>>> GetVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(MetadataUrl(), cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<IReadOnlyList<string>>.Fail(text.Error!);
        var list = ParseVersions(text.Value!);
        return list is null
            ? ServiceResult<IReadOnlyList<string>>.Fail(
                "Unexpected format of NeoForge maven-metadata.xml (not a Maven version list).")
            : ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    public async Task<ServiceResult<NeoForgeResolvedInstaller>> ResolveInstallerAsync(
        string minecraftVersion,
        string? neoForgeVersion = null,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetVersionsAsync(cancellationToken).ConfigureAwait(false);
        if (!versions.Succeeded)
            return ServiceResult<NeoForgeResolvedInstaller>.Fail(versions.Error!);
        return Resolve(minecraftVersion, versions.Value!, neoForgeVersion);
    }

    public static IReadOnlyList<string>? ParseVersions(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            var doc = XDocument.Load(reader, LoadOptions.None);
            var versions = doc.Descendants()
                .Where(e => e.Name.LocalName == "version")
                .Select(e => (e.Value ?? "").Trim())
                .Where(v => v.Length > 0)
                .ToList();
            return versions;
        }
        catch (XmlException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static ServiceResult<NeoForgeResolvedInstaller> Resolve(
        string minecraftVersion,
        IReadOnlyList<string> versions,
        string? neoForgeVersion = null)
    {
        var mc = (minecraftVersion ?? "").Trim();
        if (string.IsNullOrWhiteSpace(mc))
            return ServiceResult<NeoForgeResolvedInstaller>.Fail("Minecraft version is required.");

        if (!IsSupportedMinecraft(mc))
        {
            return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                $"NeoForge is not supported for Minecraft {mc} or older. "
                + "Minecraft 1.20.2 is the NeoForge floor; use Forge for 1.20.1 packs.");
        }

        if (!TryMinecraftTarget(mc, out var mcMinor, out var mcPatch))
        {
            return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                $"Cannot map Minecraft {mc} to a NeoForge version prefix.");
        }

        var pin = string.IsNullOrWhiteSpace(neoForgeVersion) ? null : neoForgeVersion.Trim();
        string chosen;
        if (pin is null)
        {
            var candidates = versions
                .Select(ParseNeoForgeVersion)
                .Where(v => v is not null && v.McMinor == mcMinor && v.McPatch == mcPatch && !v.IsPrerelease)
                .Cast<NeoForgeVersionId>()
                .ToList();
            if (candidates.Count == 0)
            {
                return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                    $"No stable (non-beta) NeoForge version is published for Minecraft {mc}.");
            }

            chosen = candidates.OrderByDescending(v => v).First().Raw;
        }
        else
        {
            var parsed = ParseNeoForgeVersion(pin);
            if (parsed is null)
            {
                return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                    $"NeoForge version {pin} is not a valid Maven version id.");
            }

            if (parsed.McMinor != mcMinor || parsed.McPatch != mcPatch)
            {
                return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                    $"NeoForge {pin} does not target Minecraft {mc}.");
            }

            var match = versions.FirstOrDefault(v =>
                string.Equals(v.Trim(), pin, StringComparison.Ordinal));
            if (match is null)
            {
                return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                    $"NeoForge {pin} was not found in maven.neoforged.net metadata.");
            }

            chosen = pin;
        }

        var url = InstallerUrl(chosen);
        if (!url.Contains("/neoforge-", StringComparison.Ordinal)
            || !url.EndsWith("-installer.jar", StringComparison.Ordinal)
            || !url.StartsWith(MavenBase, StringComparison.Ordinal))
        {
            return ServiceResult<NeoForgeResolvedInstaller>.Fail(
                "Refusing NeoForge installer URL that is not maven.neoforged.net installer.jar.");
        }

        return ServiceResult<NeoForgeResolvedInstaller>.Ok(new NeoForgeResolvedInstaller(
            minecraftVersion: mc,
            loaderVersion: chosen,
            installerFilename: InstallerFilename(chosen),
            installerDownloadUrl: url,
            unixArgsPath: UnixArgsPath(chosen),
            javaMajor: JavaMajorForMinecraft(mc)));
    }

    /// <summary>Minecraft 1.20.2 is the supported floor (§19.2). 1.20.1 and older refuse.</summary>
    public static bool IsSupportedMinecraft(string minecraftVersion)
    {
        if (!TryParseMinecraft(minecraftVersion, out var major, out var minor, out var patch))
            return false;
        if (major >= 26)
            return true;
        if (major != 1)
            return false;
        if (minor > 20)
            return true;
        if (minor < 20)
            return false;
        return patch >= 2;
    }

    /// <summary>
    /// Static Minecraft Java floor (§9.1 / §19.4). 17 for 1.20.2–1.20.4, 21 for 1.20.5–1.21.x, 25 for 26.1+.
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
        if (StartsWithMc(id, "1.20.2") || StartsWithMc(id, "1.20.3") || StartsWithMc(id, "1.20.4")
            || StartsWithMc(id, "1.18") || StartsWithMc(id, "1.19") || StartsWithMc(id, "1.20"))
            return 17;
        if (StartsWithMc(id, "1.17"))
            return 16;
        return 21;
    }

    public static bool TryMinecraftTarget(string minecraftVersion, out int neoMinor, out int neoPatch)
    {
        neoMinor = 0;
        neoPatch = 0;
        if (!TryParseMinecraft(minecraftVersion, out var major, out var minor, out var patch))
            return false;
        if (major >= 26)
        {
            neoMinor = major;
            neoPatch = minor;
            return true;
        }

        if (major != 1)
            return false;
        neoMinor = minor;
        neoPatch = patch;
        return true;
    }

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

    public static NeoForgeVersionId? ParseNeoForgeVersion(string raw)
    {
        var id = (raw ?? "").Trim();
        if (id.Length == 0)
            return null;
        var dash = id.IndexOf('-');
        var numeric = dash >= 0 ? id[..dash] : id;
        var pre = dash >= 0 ? id[(dash + 1)..] : "";
        var parts = numeric.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return null;
        if (!int.TryParse(parts[0], out var mcMinor)
            || !int.TryParse(parts[1], out var mcPatch)
            || !int.TryParse(parts[2], out var build))
            return null;
        return new NeoForgeVersionId(mcMinor, mcPatch, build, pre.Length > 0, id);
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
                    $"could not reach maven.neoforged.net (HTTP {code} from {url}).");
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
            $"could not reach maven.neoforged.net ({detail}).");
    }
}

public sealed class NeoForgeResolvedInstaller
{
    public NeoForgeResolvedInstaller(
        string minecraftVersion,
        string loaderVersion,
        string installerFilename,
        string installerDownloadUrl,
        string unixArgsPath,
        int javaMajor)
    {
        MinecraftVersion = minecraftVersion;
        LoaderVersion = loaderVersion;
        InstallerFilename = installerFilename;
        InstallerDownloadUrl = installerDownloadUrl;
        UnixArgsPath = unixArgsPath;
        JavaMajor = javaMajor;
    }

    public string MinecraftVersion { get; }
    public string LoaderVersion { get; }
    public string InstallerFilename { get; }
    public string InstallerDownloadUrl { get; }
    public string UnixArgsPath { get; }
    public int JavaMajor { get; }
    public string HashAlgorithm => NeoForgeMavenClient.HashAlgorithm;
    public string ArtifactKind => NeoForgeMavenClient.ArtifactKind;
    public string Loader => NeoForgeMavenClient.LoaderId;
}

public sealed record NeoForgeVersionId(int McMinor, int McPatch, int Build, bool IsPrerelease, string Raw)
    : IComparable<NeoForgeVersionId>
{
    public int CompareTo(NeoForgeVersionId? other)
    {
        if (other is null)
            return 1;
        var c = McMinor.CompareTo(other.McMinor);
        if (c != 0)
            return c;
        c = McPatch.CompareTo(other.McPatch);
        if (c != 0)
            return c;
        c = Build.CompareTo(other.Build);
        if (c != 0)
            return c;
        return Comparer<bool>.Default.Compare(other.IsPrerelease, IsPrerelease);
    }
}
