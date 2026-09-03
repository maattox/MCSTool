using System.Text.Json;
using System.Text.Json.Serialization;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Fabric meta.fabricmc.net v2 client (blueprint §18). Resolves game + loader +
/// installer (all three required) to the assembled server launcher jar URL.
/// No checksum is published — <c>none_published</c>, never a locally computed hash.
/// </summary>
public sealed class FabricMetaClient
{
    public const string MetaBase = "https://meta.fabricmc.net";
    public const string UserAgent = "MCSTool/0.1 (https://github.com/maattox/MCSTool)";
    public const string HashAlgorithm = "none_published";
    public const string ArtifactKind = "launcher_jar";
    public const string LoaderId = "fabric";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public FabricMetaClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        EnsureUserAgent(_http);
    }

    public static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public static string InstallerListUrl() => $"{MetaBase}/v2/versions/installer";

    public static string LoaderForGameUrl(string minecraftVersion) =>
        $"{MetaBase}/v2/versions/loader/{Encode(minecraftVersion)}";

    /// <summary>
    /// Assembled server launcher download. Omitting the installer segment is a
    /// confirmed-in-the-wild integration bug — all three axes are required.
    /// </summary>
    public static string ServerJarUrl(string minecraftVersion, string loaderVersion, string installerVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(loaderVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(installerVersion);
        return $"{MetaBase}/v2/versions/loader/{Encode(minecraftVersion)}/{Encode(loaderVersion)}/{Encode(installerVersion)}/server/jar";
    }

    public static string LauncherFilename(string minecraftVersion, string loaderVersion, string installerVersion) =>
        $"fabric-server-mc.{minecraftVersion.Trim()}-loader.{loaderVersion.Trim()}-launcher.{installerVersion.Trim()}.jar";

    public async Task<ServiceResult<IReadOnlyList<FabricInstallerVersion>>> GetInstallersAsync(
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(InstallerListUrl(), cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<IReadOnlyList<FabricInstallerVersion>>.Fail(text.Error!);
        var list = ParseInstallers(text.Value!);
        return list is null
            ? ServiceResult<IReadOnlyList<FabricInstallerVersion>>.Fail("Fabric installer JSON deserialized to null.")
            : ServiceResult<IReadOnlyList<FabricInstallerVersion>>.Ok(list);
    }

    public async Task<ServiceResult<IReadOnlyList<FabricGameLoaderEntry>>> GetLoadersForGameAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(LoaderForGameUrl(minecraftVersion), cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<IReadOnlyList<FabricGameLoaderEntry>>.Fail(text.Error!);
        var list = ParseGameLoaders(text.Value!);
        return list is null
            ? ServiceResult<IReadOnlyList<FabricGameLoaderEntry>>.Fail("Fabric loader-for-game JSON deserialized to null.")
            : ServiceResult<IReadOnlyList<FabricGameLoaderEntry>>.Ok(list);
    }

    public async Task<ServiceResult<FabricResolvedLauncher>> ResolveLauncherAsync(
        string minecraftVersion,
        string? loaderVersion = null,
        string? installerVersion = null,
        CancellationToken cancellationToken = default)
    {
        var installers = await GetInstallersAsync(cancellationToken).ConfigureAwait(false);
        if (!installers.Succeeded)
            return ServiceResult<FabricResolvedLauncher>.Fail(installers.Error!);
        var loaders = await GetLoadersForGameAsync(minecraftVersion, cancellationToken).ConfigureAwait(false);
        if (!loaders.Succeeded)
            return ServiceResult<FabricResolvedLauncher>.Fail(loaders.Error!);
        return Resolve(minecraftVersion, loaders.Value!, installers.Value!, loaderVersion, installerVersion);
    }

    public static IReadOnlyList<FabricInstallerVersion>? ParseInstallers(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<FabricInstallerVersion>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<FabricGameLoaderEntry>? ParseGameLoaders(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<FabricGameLoaderEntry>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static FabricInstallerVersion? SelectStableInstaller(IEnumerable<FabricInstallerVersion> installers) =>
        installers.FirstOrDefault(i => i.Stable && !string.IsNullOrWhiteSpace(i.Version));

    public static FabricGameLoaderEntry? SelectStableLoader(IEnumerable<FabricGameLoaderEntry> loaders) =>
        loaders.FirstOrDefault(e => e.Loader.Stable && !string.IsNullOrWhiteSpace(e.Loader.Version));

    public static ServiceResult<FabricResolvedLauncher> Resolve(
        string minecraftVersion,
        IReadOnlyList<FabricGameLoaderEntry> loaders,
        IReadOnlyList<FabricInstallerVersion> installers,
        string? loaderVersion = null,
        string? installerVersion = null)
    {
        var mc = minecraftVersion.Trim();
        if (string.IsNullOrWhiteSpace(mc))
            return ServiceResult<FabricResolvedLauncher>.Fail("Minecraft version is required.");

        var loaderPin = string.IsNullOrWhiteSpace(loaderVersion) ? null : loaderVersion.Trim();
        string loader;
        if (loaderPin is null)
        {
            var stable = SelectStableLoader(loaders);
            if (stable is null)
            {
                return ServiceResult<FabricResolvedLauncher>.Fail(
                    $"No stable Fabric loader for Minecraft {mc}. Unstable loaders are not installed automatically.");
            }

            loader = stable.Loader.Version.Trim();
        }
        else
        {
            var match = loaders.FirstOrDefault(e =>
                string.Equals(e.Loader.Version.Trim(), loaderPin, StringComparison.Ordinal));
            if (match is null)
            {
                return ServiceResult<FabricResolvedLauncher>.Fail(
                    $"Fabric loader {loaderPin} is not valid for Minecraft {mc}.");
            }

            loader = loaderPin;
        }

        var installerPin = string.IsNullOrWhiteSpace(installerVersion) ? null : installerVersion.Trim();
        string installer;
        if (installerPin is null)
        {
            var stable = SelectStableInstaller(installers);
            if (stable is null)
            {
                return ServiceResult<FabricResolvedLauncher>.Fail(
                    "No stable Fabric installer version is published.");
            }

            installer = stable.Version.Trim();
        }
        else
        {
            var match = installers.FirstOrDefault(i =>
                string.Equals(i.Version.Trim(), installerPin, StringComparison.Ordinal));
            if (match is null)
            {
                return ServiceResult<FabricResolvedLauncher>.Fail(
                    $"Fabric installer {installerPin} was not found in meta.fabricmc.net installer list.");
            }

            installer = installerPin;
        }

        if (string.IsNullOrWhiteSpace(loader) || string.IsNullOrWhiteSpace(installer))
        {
            return ServiceResult<FabricResolvedLauncher>.Fail(
                "Fabric server jar URL requires game, loader, and installer versions.");
        }

        var url = ServerJarUrl(mc, loader, installer);
        if (!url.EndsWith("/server/jar", StringComparison.Ordinal)
            || CountVersionAxes(url) != 3)
        {
            return ServiceResult<FabricResolvedLauncher>.Fail(
                "Refusing Fabric download URL that omits game, loader, or installer.");
        }

        return ServiceResult<FabricResolvedLauncher>.Ok(new FabricResolvedLauncher(
            minecraftVersion: mc,
            loaderVersion: loader,
            installerVersion: installer,
            filename: LauncherFilename(mc, loader, installer),
            downloadUrl: url,
            javaMajor: JavaMajorForMinecraft(mc)));
    }

    /// <summary>
    /// Fabric meta has no per-version Java floor API. Static Minecraft table (§9.1).
    /// Do not use launcherMeta.min_java_version (that is the launcher's own floor, often 8).
    /// </summary>
    public static int JavaMajorForMinecraft(string minecraftVersion) =>
        MinecraftJavaFloor.TryGet(minecraftVersion, out var major) ? major : 21;

    public static int CountVersionAxes(string serverJarUrl)
    {
        // .../v2/versions/loader/{game}/{loader}/{installer}/server/jar
        if (!Uri.TryCreate(serverJarUrl, UriKind.Absolute, out var uri))
            return 0;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var loaderIdx = Array.IndexOf(parts, "loader");
        var serverIdx = Array.IndexOf(parts, "server");
        if (loaderIdx < 0 || serverIdx < 0 || serverIdx != parts.Length - 2)
            return 0;
        return serverIdx - loaderIdx - 1;
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
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return ServiceResult<string>.Fail($"Fabric meta HTTP {(int)response.StatusCode} from {url}.");
        return ServiceResult<string>.Ok(json);
    }
}

public sealed class FabricResolvedLauncher
{
    public FabricResolvedLauncher(
        string minecraftVersion,
        string loaderVersion,
        string installerVersion,
        string filename,
        string downloadUrl,
        int javaMajor)
    {
        MinecraftVersion = minecraftVersion;
        LoaderVersion = loaderVersion;
        InstallerVersion = installerVersion;
        Filename = filename;
        DownloadUrl = downloadUrl;
        JavaMajor = javaMajor;
    }

    public string MinecraftVersion { get; }
    public string LoaderVersion { get; }
    public string InstallerVersion { get; }
    public string Filename { get; }
    public string DownloadUrl { get; }
    public int JavaMajor { get; }
    public string HashAlgorithm => FabricMetaClient.HashAlgorithm;
    public string ArtifactKind => FabricMetaClient.ArtifactKind;
    public string Loader => FabricMetaClient.LoaderId;
}

public sealed class FabricInstallerVersion
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("maven")]
    public string Maven { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

public sealed class FabricGameLoaderEntry
{
    [JsonPropertyName("loader")]
    public FabricLoaderInfo Loader { get; set; } = new();

    [JsonPropertyName("intermediary")]
    public FabricIntermediaryInfo Intermediary { get; set; } = new();
}

public sealed class FabricLoaderInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }

    [JsonPropertyName("maven")]
    public string Maven { get; set; } = "";
}

public sealed class FabricIntermediaryInfo
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}
