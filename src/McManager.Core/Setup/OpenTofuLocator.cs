using System.IO.Compression;
using System.Security.Cryptography;
using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>
/// Pinned OpenTofu Windows amd64 zip (not a live “latest” float).
/// Checksums: https://github.com/opentofu/opentofu/releases/download/v1.12.6/tofu_1.12.6_SHA256SUMS
/// </summary>
public sealed record OpenTofuDownloadPin(string Version, string ZipUrl, string Sha256Hex)
{
    public static OpenTofuDownloadPin Product { get; } = new(
        "1.12.6",
        "https://github.com/opentofu/opentofu/releases/download/v1.12.6/tofu_1.12.6_windows_amd64.zip",
        "0d1421721cf9ec24b41b698a9620dda218d47fa7e76ac3dc15cdbc13bd79b0bb");
}

/// <summary>
/// Finds a local OpenTofu binary (PATH, WinGet Links, LocalAppData). If still missing,
/// downloads a pinned Windows amd64 zip, verifies SHA-256, and extracts <c>tofu.exe</c>.
/// Never downloads HashiCorp <c>terraform.exe</c>.
/// </summary>
public static class OpenTofuLocator
{
    public const string UserAgent = "McManager/0.1 (https://github.com/maattox/MCSTool)";
    public const string SourceUrl = "https://github.com/opentofu/opentofu";
    public const string MplLicenseUrl = "https://www.mozilla.org/MPL/2.0/";
    public const string ExeFileName = "tofu.exe";
    public const string LicenseFileName = "LICENSE-OpenTofu.txt";
    public const int HttpTimeoutSeconds = 180;
    internal const long MaxZipBytes = 80L * 1024 * 1024;

    public static string DefaultInstallDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppSettingsStore.ProductFolderName,
            "tofu");

    public static string DefaultExePath => Path.Combine(DefaultInstallDirectory, ExeFileName);

    public static string? Find() =>
        Find(DefaultInstallDirectory, searchPathAndWinget: true);

    internal static string? Find(string bundledDirectory, bool searchPathAndWinget = true)
    {
        if (searchPathAndWinget)
        {
            var fromPath = FindOnPath(ExeFileName) ?? FindOnPath("tofu");
            if (fromPath is not null)
                return fromPath;

            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var winget = Path.Combine(local, "Microsoft", "WinGet", "Links", ExeFileName);
            if (File.Exists(winget))
                return winget;
        }

        if (string.IsNullOrWhiteSpace(bundledDirectory))
            return null;

        var bundled = Path.Combine(bundledDirectory, ExeFileName);
        return File.Exists(bundled) ? bundled : null;
    }

    public static string MissingMessage() =>
        "OpenTofu (tofu.exe) was not found and could not be downloaded. "
        + "The first Setup needs internet to fetch a pinned OpenTofu Windows build into "
        + @"%LOCALAPPDATA%\" + AppSettingsStore.ProductFolderName + @"\tofu. Check the connection and try again. "
        + "OpenTofu is MPL 2.0 (" + SourceUrl + ").";

    public static async Task<IOpenTofuRunner> CreateRunnerAsync(
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var path = await EnsureAsync(log: log, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new OpenTofuRunner(path);
    }

    /// <summary>
    /// Returns PATH / WinGet / existing LocalAppData tofu, or downloads the pinned zip once.
    /// Pass <paramref name="installDirectory"/> in tests to skip PATH/WinGet and isolate the dest folder.
    /// </summary>
    public static async Task<string> EnsureAsync(
        HttpClient? http = null,
        string? installDirectory = null,
        OpenTofuDownloadPin? pin = null,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var dest = string.IsNullOrWhiteSpace(installDirectory)
            ? DefaultInstallDirectory
            : installDirectory;
        var searchPath = installDirectory is null;
        var existing = Find(dest, searchPathAndWinget: searchPath);
        if (existing is not null)
        {
            log?.Report("Using OpenTofu at " + existing);
            return existing;
        }

        var ownsHttp = http is null;
        http ??= CreateHttp();
        try
        {
            return await DownloadPinnedAsync(
                    http,
                    dest,
                    pin ?? OpenTofuDownloadPin.Product,
                    log,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsHttp)
                http.Dispose();
        }
    }

    internal static async Task<string> DownloadPinnedAsync(
        HttpClient http,
        string installDirectory,
        OpenTofuDownloadPin pin,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(pin);

        if (string.IsNullOrWhiteSpace(pin.ZipUrl)
            || string.IsNullOrWhiteSpace(pin.Sha256Hex)
            || pin.ZipUrl.Contains("hashicorp", StringComparison.OrdinalIgnoreCase)
            || (pin.ZipUrl.Contains("terraform", StringComparison.OrdinalIgnoreCase)
                && !pin.ZipUrl.Contains("opentofu", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Refusing to download HashiCorp terraform.exe. This product uses OpenTofu only.");
        }

        EnsureUserAgent(http);
        Directory.CreateDirectory(installDirectory);

        var zipPath = Path.Combine(installDirectory, "tofu-download.zip");
        var stagingExe = Path.Combine(installDirectory, ExeFileName + ".new");
        var destExe = Path.Combine(installDirectory, ExeFileName);

        try
        {
            log?.Report($"Downloading OpenTofu {pin.Version} (Windows amd64)…");
            await DownloadToFileAsync(http, pin.ZipUrl, zipPath, cancellationToken).ConfigureAwait(false);

            var actual = Sha256FileHex(zipPath);
            var expected = pin.Sha256Hex.Trim().ToLowerInvariant();
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"OpenTofu download failed the SHA-256 check (got {actual}, expected {expected}). "
                    + "The file was not installed.");
            }

            log?.Report("OpenTofu SHA-256 verified. Installing…");
            ExtractTofuExe(zipPath, stagingExe);
            File.Move(stagingExe, destExe, overwrite: true);
            WriteLicenseNotice(installDirectory, pin.Version);
            log?.Report($"Installed OpenTofu {pin.Version} (MPL 2.0) to {destExe}");
            return destExe;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                   or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(MissingMessage() + " " + ex.Message, ex);
        }
        finally
        {
            TryDelete(zipPath);
            TryDelete(stagingExe);
        }
    }

    internal static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    internal static HttpClient CreateHttp() =>
        new() { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };

    internal static string Sha256FileHex(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    internal static string LicenseNotice(string version) =>
        "OpenTofu " + version + " is licensed under the Mozilla Public License 2.0." + Environment.NewLine
        + "Source: " + SourceUrl + Environment.NewLine
        + "License: " + MplLicenseUrl + Environment.NewLine
        + "MCSTool downloads this pinned Windows amd64 build once into this folder."
        + Environment.NewLine;

    private static async Task DownloadToFileAsync(
        HttpClient http,
        string url,
        string destPath,
        CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                MissingMessage() + $" HTTP {(int)response.StatusCode}.");
        }

        var length = response.Content.Headers.ContentLength;
        if (length is > MaxZipBytes)
        {
            throw new InvalidOperationException(
                $"OpenTofu zip is {length} bytes, over the {MaxZipBytes} byte limit. The file was not installed.");
        }

        await using var fs = new FileStream(
            destPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous);
        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var copied = 0L;
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;
            copied += read;
            if (copied > MaxZipBytes)
            {
                throw new InvalidOperationException(
                    $"OpenTofu zip exceeded the {MaxZipBytes} byte limit. The file was not installed.");
            }

            await fs.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ExtractTofuExe(string zipPath, string destExe)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? tofu = null;
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name))
                continue;
            var name = Path.GetFileName(entry.FullName);
            if (name.Equals(ExeFileName, StringComparison.OrdinalIgnoreCase))
            {
                tofu = entry;
                break;
            }
        }

        if (tofu is null)
        {
            throw new InvalidOperationException(
                "OpenTofu zip did not contain tofu.exe. The file was not installed.");
        }

        tofu.ExtractToFile(destExe, overwrite: true);
    }

    private static void WriteLicenseNotice(string installDirectory, string version) =>
        File.WriteAllText(Path.Combine(installDirectory, LicenseFileName), LicenseNotice(version));

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // skip invalid PATH entries
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // best-effort
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort
        }
    }
}
