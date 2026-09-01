using System.Net;
using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class CrafatarAvatarCacheTests
{
    private static readonly byte[] OnePxPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwADhQGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly byte[] OtherPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEklEQVR42mP8z8BQz8DA8J8BAAKfA/0c8Vq9AAAAAElFTkSuQmCC");

    private const string SteveHyphenless = "069a79f444e94726a5befca90e38aaf5";

    [Fact]
    public void Avatar_url_is_crafatar_hyphenless_with_overlay()
    {
        Assert.Equal(
            "https://crafatar.com/avatars/069a79f444e94726a5befca90e38aaf5?size=32&overlay",
            CrafatarAvatarCache.AvatarUrl(SteveHyphenless));
        Assert.Equal(
            "https://minotar.net/helm/069a79f444e94726a5befca90e38aaf5/32",
            CrafatarAvatarCache.MinotarHelmUrl(SteveHyphenless));
    }

    [Fact]
    public async Task Missing_uuid_does_not_call_http()
    {
        var handler = new BytesHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var dir = NewTempDir();
        try
        {
            var cache = new CrafatarAvatarCache(dir, http);
            Assert.Null(await cache.TryGetDataUrlAsync("not-a-uuid"));
            Assert.Empty(handler.Requests);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Fresh_cache_file_skips_http()
    {
        var handler = new BytesHandler();
        handler.Map(CrafatarAvatarCache.AvatarUrl(SteveHyphenless), OnePxPng);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var dir = NewTempDir();
        try
        {
            var cache = new CrafatarAvatarCache(dir, http, TimeSpan.FromHours(12));
            var first = await cache.TryGetDataUrlAsync(SteveHyphenless);
            Assert.NotNull(first);
            Assert.StartsWith("data:image/png;base64,", first, StringComparison.Ordinal);
            Assert.Single(handler.Requests);

            var second = await cache.TryGetDataUrlAsync("069a79f4-44e9-4726-a5be-fca90e38aaf5");
            Assert.Equal(first, second);
            Assert.Single(handler.Requests);
            Assert.Contains("MCSTool", handler.LastUserAgent, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Crafatar_success_does_not_call_minotar()
    {
        var handler = new BytesHandler();
        handler.Map(CrafatarAvatarCache.AvatarUrl(SteveHyphenless), OnePxPng);
        handler.Map(CrafatarAvatarCache.MinotarHelmUrl(SteveHyphenless), OtherPng);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var dir = NewTempDir();
        try
        {
            var cache = new CrafatarAvatarCache(dir, http);
            var url = await cache.TryGetDataUrlAsync(SteveHyphenless);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(OnePxPng), url);
            Assert.Single(handler.Requests);
            Assert.Equal(CrafatarAvatarCache.AvatarUrl(SteveHyphenless), handler.Requests[0].ToString());
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Crafatar_500_png_falls_back_to_minotar_helm_and_caches()
    {
        var handler = new BytesHandler();
        handler.Map(CrafatarAvatarCache.AvatarUrl(SteveHyphenless), OnePxPng, HttpStatusCode.InternalServerError);
        handler.Map(CrafatarAvatarCache.MinotarHelmUrl(SteveHyphenless), OtherPng);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var dir = NewTempDir();
        try
        {
            var cache = new CrafatarAvatarCache(dir, http);
            var url = await cache.TryGetDataUrlAsync(SteveHyphenless);
            Assert.Equal("data:image/png;base64," + Convert.ToBase64String(OtherPng), url);
            Assert.Equal(2, handler.Requests.Count);

            var again = await cache.TryGetDataUrlAsync(SteveHyphenless);
            Assert.Equal(url, again);
            Assert.Equal(2, handler.Requests.Count);
            Assert.True(File.Exists(Path.Combine(dir, SteveHyphenless + ".png")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Both_sources_missing_returns_null_and_does_not_write_cache()
    {
        var handler = new BytesHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var dir = NewTempDir();
        try
        {
            var cache = new CrafatarAvatarCache(dir, http);
            Assert.Null(await cache.TryGetDataUrlAsync(SteveHyphenless));
            Assert.Empty(Directory.EnumerateFiles(dir, "*.png"));
            Assert.Null(await cache.TryGetDataUrlAsync(SteveHyphenless));
            Assert.Equal(2, handler.Requests.Count);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-crafatar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _map =
            new(StringComparer.Ordinal);

        public List<Uri> Requests { get; } = [];

        public string LastUserAgent { get; private set; } = "";

        public void Map(string url, byte[] body, HttpStatusCode status = HttpStatusCode.OK) =>
            _map[url] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUserAgent = request.Headers.UserAgent.ToString();
            Requests.Add(request.RequestUri!);
            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var mapped))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent([]),
                });
            }

            return Task.FromResult(new HttpResponseMessage(mapped.Status)
            {
                Content = new ByteArrayContent(mapped.Body),
            });
        }
    }
}
