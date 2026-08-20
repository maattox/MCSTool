using System.Net;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ExcludeIncludeListRefreshTests
{
    [Fact]
    public void Valid_github_json_replaces_embedded_layer1()
    {
        var handler = new MapHandler();
        handler.Map(
            ExcludeIncludeListRefresh.ModrinthRawUrl,
            """{"globalExcludes":["unique-test-mod"],"globalForceIncludes":[],"modpacks":{}}""");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var refresh = new ExcludeIncludeListRefresh(http);
        var matcher = refresh.ModrinthMatcher();

        Assert.True(refresh.UsedRemote(ExcludeIncludeListRefresh.ModrinthRawUrl));
        Assert.Equal(ExcludeIncludeDecision.Exclude, matcher.Match(null, "mods/unique-test-mod-1.jar").Decision);
        Assert.Equal(ExcludeIncludeDecision.NoMatch, matcher.Match(null, "mods/sodium-0.5.jar").Decision);
    }

    [Fact]
    public void Timeout_non_json_or_empty_excludes_fall_back_to_embedded()
    {
        AssertFallback(new DelayHandler(TimeSpan.FromSeconds(30)), TimeSpan.FromMilliseconds(200));
        AssertFallback(new MapHandler().With(ExcludeIncludeListRefresh.ModrinthRawUrl, "<html>not json</html>"));
        AssertFallback(new MapHandler().With(
            ExcludeIncludeListRefresh.ModrinthRawUrl,
            """{"globalExcludes":[],"globalForceIncludes":[],"modpacks":{}}"""));
        AssertFallback(new StatusHandler(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public void Curseforge_refresh_failure_does_not_throw()
    {
        using var http = new HttpClient(new FailHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        var refresh = new ExcludeIncludeListRefresh(http);
        var matcher = refresh.CurseForgeMatcher();
        Assert.False(refresh.UsedRemote(ExcludeIncludeListRefresh.CurseForgeRawUrl));
        Assert.Equal(ExcludeIncludeDecision.Exclude, matcher.Match(null, "mods/embeddium-1.20.1.jar").Decision);
    }

    private static void AssertFallback(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(2) };
        var refresh = new ExcludeIncludeListRefresh(http);
        var matcher = refresh.ModrinthMatcher();
        Assert.False(refresh.UsedRemote(ExcludeIncludeListRefresh.ModrinthRawUrl));
        Assert.Equal(ExcludeIncludeDecision.Exclude, matcher.Match(null, "mods/sodium-0.5.jar").Decision);
    }

    private sealed class FailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("down"));
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent("error", Encoding.UTF8, "text/plain"),
            });
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public DelayHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"globalExcludes":["too-late"]}""", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

        public MapHandler With(string url, string body)
        {
            Map(url, body);
            return this;
        }

        public void Map(string url, string body) => _map[url] = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"unmapped {url}"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
