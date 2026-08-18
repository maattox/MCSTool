using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Installs a user-supplied Modrinth <c>.mrpack</c> into a destination directory
/// (blueprint §22). Plain GET of URLs already in the index — no catalog/search API.
/// Strips <c>env.server == unsupported</c>; fails loudly when side is unclear.
/// </summary>
public sealed class MrpackInstaller
{
    public const string UserAgent = "McManager/0.1 (https://github.com/maattox/oci-mc-server)";
    public const int HttpTimeoutSeconds = 120;
    public const string OverridesPrefix = "overrides/";
    public const string ServerOverridesPrefix = "server-overrides/";
    public const string ClientOverridesPrefix = "client-overrides/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HttpClient _http;

    public MrpackInstaller(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        EnsureUserAgent(_http);
    }

    public static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<ServiceResult<MrpackInstallResult>> InstallAsync(
        string mrpackPath,
        string destDirectory,
        string? retainDataDirectory,
        CancellationToken cancellationToken = default)
    {
        var analysisResult = MrpackAnalyzer.AnalyzeFile(mrpackPath);
        if (!analysisResult.Succeeded)
            return ServiceResult<MrpackInstallResult>.Fail(analysisResult.Error!);

        var analysis = analysisResult.Value!;
        if (analysis.UnclearSideCount > 0)
        {
            var listed = string.Join(Environment.NewLine, analysis.UnclearSidePaths.Select(p => "  " + p));
            return ServiceResult<MrpackInstallResult>.Fail(
                $"Cannot install this pack: {analysis.UnclearSideCount} file(s) have unclear "
                + "server/client side (missing or unknown env.server). Do not guess. Review:"
                + Environment.NewLine + listed);
        }

        if (string.IsNullOrWhiteSpace(destDirectory))
            return ServiceResult<MrpackInstallResult>.Fail("No install destination directory was provided.");

        try
        {
            Directory.CreateDirectory(destDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ServiceResult<MrpackInstallResult>.Fail($"Cannot create destination: {ex.Message}");
        }

        List<MrpackIndexFile> files;
        try
        {
            using var stream = File.OpenRead(mrpackPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var indexEntry = zip.Entries.FirstOrDefault(e =>
                string.Equals(
                    MrpackAnalyzer.NormalizeZipPath(e.FullName),
                    MrpackAnalyzer.IndexEntryName,
                    StringComparison.OrdinalIgnoreCase));
            if (indexEntry is null)
            {
                return ServiceResult<MrpackInstallResult>.Fail(
                    $"Not a Modrinth pack: missing {MrpackAnalyzer.IndexEntryName}.");
            }

            string json;
            using (var reader = new StreamReader(indexEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                json = reader.ReadToEnd();

            var index = JsonSerializer.Deserialize<MrpackIndexDocument>(json, JsonOptions);
            files = index?.Files ?? [];

            var installed = new List<string>();
            var skippedClientOnly = new List<string>();
            var warnings = new List<string>(analysis.Warnings);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = string.IsNullOrWhiteSpace(file.Path)
                    ? ""
                    : file.Path.Trim().Replace('\\', '/');
                var label = relative.Length == 0 ? "(unnamed file)" : relative;
                var serverEnv = (file.Env?.Server ?? "").Trim();

                if (serverEnv.Equals(MrpackAnalyzer.EnvUnsupported, StringComparison.OrdinalIgnoreCase))
                {
                    skippedClientOnly.Add(label);
                    continue;
                }

                if (!serverEnv.Equals(MrpackAnalyzer.EnvRequired, StringComparison.OrdinalIgnoreCase)
                    && !serverEnv.Equals(MrpackAnalyzer.EnvOptional, StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult<MrpackInstallResult>.Fail(
                        $"Cannot install this pack: file '{label}' has unclear env.server.");
                }

                var destPath = ResolveUnderDest(destDirectory, relative);
                if (!destPath.Succeeded)
                    return ServiceResult<MrpackInstallResult>.Fail(destPath.Error!);

                var downloaded = await DownloadVerifiedAsync(
                    file,
                    label,
                    destPath.Value!,
                    cancellationToken).ConfigureAwait(false);
                if (!downloaded.Succeeded)
                    return ServiceResult<MrpackInstallResult>.Fail(downloaded.Error!);

                installed.Add(relative);
            }

            var copiedOverrides = new List<string>();
            var overridesResult = CopyOverrideTree(zip, destDirectory, OverridesPrefix, copiedOverrides);
            if (!overridesResult.Succeeded)
                return ServiceResult<MrpackInstallResult>.Fail(overridesResult.Error!);
            var serverOverridesResult = CopyOverrideTree(
                zip, destDirectory, ServerOverridesPrefix, copiedOverrides);
            if (!serverOverridesResult.Succeeded)
                return ServiceResult<MrpackInstallResult>.Fail(serverOverridesResult.Error!);

            string? retained = null;
            if (!string.IsNullOrWhiteSpace(retainDataDirectory))
            {
                var retain = ImportedPackArchiveStore.Retain(mrpackPath, analysis, retainDataDirectory);
                if (!retain.Succeeded)
                    return ServiceResult<MrpackInstallResult>.Fail(retain.Error!);
                retained = retain.Value;
            }
            else
            {
                warnings.Add("Original .mrpack was not copied into Manager local data (no data directory).");
            }

            var summary = BuildSummary(
                analysis,
                destDirectory,
                retained,
                installed,
                skippedClientOnly,
                copiedOverrides,
                warnings);

            return ServiceResult<MrpackInstallResult>.Ok(new MrpackInstallResult(
                analysis,
                Path.GetFullPath(destDirectory),
                retained,
                installed,
                skippedClientOnly,
                copiedOverrides,
                warnings,
                summary));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ServiceResult<MrpackInstallResult>.Fail($"Cannot install .mrpack: {ex.Message}");
        }
    }

    public static ServiceResult<string> ResolveUnderDest(string destDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return ServiceResult<string>.Fail("Index file path is empty.");

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.StartsWith('/') || Path.IsPathRooted(relativePath))
        {
            return ServiceResult<string>.Fail(
                $"Refusing path '{relativePath}': must be relative to the install directory.");
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0
            || parts.Any(p => p is ".." or "." || p.Contains(':')))
        {
            return ServiceResult<string>.Fail($"Refusing unsafe pack path '{relativePath}'.");
        }

        var destRoot = Path.GetFullPath(destDirectory);
        var combined = Path.GetFullPath(Path.Combine(destRoot, Path.Combine(parts)));
        var prefix = destRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(combined, destRoot, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.Fail($"Refusing path '{relativePath}': escapes the install directory.");
        }

        return ServiceResult<string>.Ok(combined);
    }

    public static bool TryGetPreferredHash(
        IReadOnlyDictionary<string, string>? hashes,
        out string algorithm,
        out string expectedHex)
    {
        algorithm = "";
        expectedHex = "";
        if (hashes is null || hashes.Count == 0)
            return false;

        if (TryFindHash(hashes, "sha512", out expectedHex))
        {
            algorithm = "sha512";
            return true;
        }

        if (TryFindHash(hashes, "sha1", out expectedHex))
        {
            algorithm = "sha1";
            return true;
        }

        return false;
    }

    internal static string HashFileHex(string path, string algorithm)
    {
        using var stream = File.OpenRead(path);
        byte[] digest = algorithm.Equals("sha512", StringComparison.OrdinalIgnoreCase)
            ? SHA512.HashData(stream)
            : SHA1.HashData(stream);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private async Task<ServiceResult> DownloadVerifiedAsync(
        MrpackIndexFile file,
        string label,
        string destPath,
        CancellationToken cancellationToken)
    {
        var urls = (file.Downloads ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToList();
        if (urls.Count == 0)
        {
            return ServiceResult.Fail(
                $"Cannot install '{label}': no download URL in the pack index.");
        }

        if (!TryGetPreferredHash(file.Hashes, out var algorithm, out var expectedHex))
        {
            return ServiceResult.Fail(
                $"Cannot install '{label}': index has no sha512 or sha1 hash to verify.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var errors = new List<string>();
        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                errors.Add($"{url}: not an http(s) URL.");
                continue;
            }

            try
            {
                using var response = await _http.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{uri}: HTTP {(int)response.StatusCode}.");
                    continue;
                }

                await using (var output = File.Create(destPath))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                var actual = HashFileHex(destPath, algorithm);
                if (!actual.Equals(expectedHex, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(destPath); } catch (IOException) { }
                    return ServiceResult.Fail(
                        $"Cannot install '{label}': {algorithm} mismatch (expected {expectedHex}, got {actual}).");
                }

                return ServiceResult.Ok();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                errors.Add($"{uri}: {ex.Message}");
            }
            catch (IOException ex)
            {
                errors.Add($"{uri}: {ex.Message}");
            }
        }

        return ServiceResult.Fail(
            $"Cannot install '{label}': every index download URL failed. "
            + string.Join(" ", errors));
    }

    private static ServiceResult CopyOverrideTree(
        ZipArchive zip,
        string destDirectory,
        string prefix,
        List<string> copiedRelativePaths)
    {
        foreach (var entry in zip.Entries)
        {
            var name = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Length <= prefix.Length)
                continue;
            if (name.EndsWith('/'))
                continue;

            var relative = name[prefix.Length..];
            var destPath = ResolveUnderDest(destDirectory, relative);
            if (!destPath.Succeeded)
                return ServiceResult.Fail(destPath.Error!);

            Directory.CreateDirectory(Path.GetDirectoryName(destPath.Value!)!);
            using var input = entry.Open();
            using var output = File.Create(destPath.Value!);
            input.CopyTo(output);
            copiedRelativePaths.Add(relative.Replace('\\', '/'));
        }

        return ServiceResult.Ok();
    }

    private static bool TryFindHash(
        IReadOnlyDictionary<string, string> hashes,
        string algorithm,
        out string hex)
    {
        hex = "";
        foreach (var pair in hashes)
        {
            if (!string.Equals(pair.Key, algorithm, StringComparison.OrdinalIgnoreCase))
                continue;
            if (string.IsNullOrWhiteSpace(pair.Value))
                return false;
            hex = pair.Value.Trim();
            return true;
        }

        return false;
    }

    private static string BuildSummary(
        MrpackAnalysis analysis,
        string destDirectory,
        string? retainedArchivePath,
        IReadOnlyList<string> installed,
        IReadOnlyList<string> skippedClientOnly,
        IReadOnlyList<string> copiedOverrides,
        IReadOnlyList<string> warnings)
    {
        var sb = new StringBuilder();
        sb.AppendLine(analysis.ConfirmableSummary);
        sb.AppendLine();
        sb.Append("Installed into: ").AppendLine(Path.GetFullPath(destDirectory));
        sb.Append("Server-side files written: ").AppendLine(installed.Count.ToString());
        foreach (var p in installed)
            sb.Append("  ").AppendLine(p);
        sb.Append("Client-only skipped: ").AppendLine(skippedClientOnly.Count.ToString());
        foreach (var p in skippedClientOnly)
            sb.Append("  ").AppendLine(p);
        if (copiedOverrides.Count > 0)
        {
            sb.Append("Overrides copied (overrides/ then server-overrides/; client-overrides skipped): ")
                .AppendLine(copiedOverrides.Count.ToString());
            foreach (var p in copiedOverrides)
                sb.Append("  ").AppendLine(p);
        }

        if (!string.IsNullOrEmpty(retainedArchivePath))
            sb.Append("Original archive retained: ").AppendLine(retainedArchivePath);
        sb.AppendLine(MrpackInstallResult.ClientPackReminder);
        if (warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in warnings)
                sb.Append("  ").AppendLine(w);
        }

        return sb.ToString().TrimEnd();
    }
}
