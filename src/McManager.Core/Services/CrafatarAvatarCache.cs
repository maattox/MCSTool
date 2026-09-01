using System.Collections.Concurrent;
using System.Net.Http.Headers;
using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Disk-cached player face PNGs (helmet/overlay). Tries Crafatar first, then Minotar
/// <c>/helm/</c> when Crafatar is 4xx/5xx. Never calls Mojang session APIs.
/// Cache lives under <c>%LOCALAPPDATA%\MCSTool\avatars\</c> unless a directory is injected.
/// </summary>
public sealed class CrafatarAvatarCache
{
    public const string CdnOrigin = "https://crafatar.com";
    public const string MinotarOrigin = "https://minotar.net";
    public const int SizePx = 32;
    public const int DefaultTtlHours = 12;
    public const string UserAgent = "MCSTool (https://github.com/maattox/MCSTool)";
    public const int HttpTimeoutSeconds = 8;

    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47];

    private readonly string _cacheDirectory;
    private readonly HttpClient _http;
    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _missUntil = new(StringComparer.OrdinalIgnoreCase);

    public CrafatarAvatarCache(
        string? cacheDirectory = null,
        HttpClient? http = null,
        TimeSpan? ttl = null)
    {
        var local = cacheDirectory
            ?? Path.Combine(LocalConfigStore.GetInstalledDataDirectory(), "avatars");
        _cacheDirectory = local;
        _ttl = ttl is { TotalHours: >= 1 and <= 24 } ? ttl.Value : TimeSpan.FromHours(DefaultTtlHours);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        EnsureHeaders(_http);
    }

    public static void EnsureHeaders(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        if (!http.DefaultRequestHeaders.Accept.Any())
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
    }

    public static string AvatarUrl(string uuidHyphenless) =>
        CdnOrigin + "/avatars/" + uuidHyphenless + "?size=" + SizePx + "&overlay";

    /// <summary>Helmet (head + overlay) at <see cref="SizePx"/>.</summary>
    public static string MinotarHelmUrl(string uuidHyphenless) =>
        MinotarOrigin + "/helm/" + uuidHyphenless + "/" + SizePx;

    /// <summary>
    /// Returns a <c>data:image/png;base64,...</c> URL, or null on 4xx/5xx/missing UUID.
    /// Skips HTTP when a fresh cache file exists.
    /// </summary>
    public async Task<string?> TryGetDataUrlAsync(string? uuid, CancellationToken cancellationToken = default)
    {
        var key = MinecraftConsoleRemote.ToHyphenlessUuid(uuid);
        if (key.Length != 32)
            return null;

        if (TryReadFreshCache(key, out var cached))
            return cached;

        if (_missUntil.TryGetValue(key, out var until) && until > DateTimeOffset.UtcNow)
            return null;

        var task = _inflight.GetOrAdd(key, k => FetchAndCacheAsync(k, cancellationToken));
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private async Task<string?> FetchAndCacheAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            if (TryReadFreshCache(key, out var cached))
                return cached;

            byte[]? errorPng = null;
            foreach (var url in new[] { AvatarUrl(key), MinotarHelmUrl(key) })
            {
                var (ok, png) = await TryGetPngAsync(url, cancellationToken).ConfigureAwait(false);
                if (ok && png is not null)
                {
                    TryWriteCache(key, png);
                    _missUntil.TryRemove(key, out _);
                    return ToDataUrl(png);
                }

                if (png is not null)
                    errorPng ??= png;
            }

            // Crafatar 5xx still ships a default Steve/Alex PNG. Show it this session
            // but do not persist it as this UUID (Minotar may recover).
            if (errorPng is not null)
                return ToDataUrl(errorPng);

            _missUntil[key] = DateTimeOffset.UtcNow + _ttl;
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _missUntil[key] = DateTimeOffset.UtcNow + _ttl;
            return null;
        }
    }

    private async Task<(bool Success, byte[]? Png)> TryGetPngAsync(
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (!IsPng(bytes))
                return (false, null);
            return (response.IsSuccessStatusCode, bytes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return (false, null);
        }
    }

    private bool TryReadFreshCache(string key, out string? dataUrl)
    {
        dataUrl = null;
        if (string.IsNullOrEmpty(_cacheDirectory))
            return false;

        var path = CachePath(key);
        try
        {
            if (!File.Exists(path))
                return false;
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(path);
            if (age < TimeSpan.Zero || age > _ttl)
                return false;
            var bytes = File.ReadAllBytes(path);
            if (!IsPng(bytes))
                return false;
            dataUrl = ToDataUrl(bytes);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void TryWriteCache(string key, byte[] bytes)
    {
        if (string.IsNullOrEmpty(_cacheDirectory))
            return;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllBytes(CachePath(key), bytes);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CachePath(string key) => Path.Combine(_cacheDirectory, key + ".png");

    private static bool IsPng(byte[] bytes) =>
        bytes.Length >= PngMagic.Length
        && bytes[0] == PngMagic[0]
        && bytes[1] == PngMagic[1]
        && bytes[2] == PngMagic[2]
        && bytes[3] == PngMagic[3];

    private static string ToDataUrl(byte[] bytes) =>
        "data:image/png;base64," + Convert.ToBase64String(bytes);
}
