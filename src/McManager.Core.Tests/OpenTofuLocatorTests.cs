using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class OpenTofuLocatorTests
{
    [Fact]
    public void Product_pin_is_opentofu_windows_amd64_not_latest_or_terraform()
    {
        var pin = OpenTofuDownloadPin.Product;
        Assert.Equal("1.12.6", pin.Version);
        Assert.Equal(
            "https://github.com/opentofu/opentofu/releases/download/v1.12.6/tofu_1.12.6_windows_amd64.zip",
            pin.ZipUrl);
        Assert.Equal(
            "0d1421721cf9ec24b41b698a9620dda218d47fa7e76ac3dc15cdbc13bd79b0bb",
            pin.Sha256Hex);
        Assert.DoesNotContain("/latest", pin.ZipUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("terraform.exe", pin.ZipUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hashicorp", pin.ZipUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Find_returns_bundled_exe_without_path_search()
    {
        var dir = NewTempDir("tofu-find");
        try
        {
            var exe = Path.Combine(dir, "tofu.exe");
            File.WriteAllText(exe, "placeholder");
            Assert.Equal(exe, OpenTofuLocator.Find(dir, searchPathAndWinget: false));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Missing_message_does_not_tell_users_to_use_winget()
    {
        var msg = OpenTofuLocator.MissingMessage();
        Assert.DoesNotContain("winget", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCALAPPDATA", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opentofu", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ensure_downloads_and_finds_extracted_exe()
    {
        var dir = NewTempDir("tofu-ok");
        try
        {
            var zip = ZipWithFile("tofu.exe", "tofu-ok"u8.ToArray());
            var pin = new OpenTofuDownloadPin(
                "test",
                "https://example.test/tofu.zip",
                Sha256Hex(zip));
            var handler = new MapHandler();
            handler.Map(pin.ZipUrl, zip);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var path = await OpenTofuLocator.EnsureAsync(http, dir, pin);

            Assert.Equal(Path.Combine(dir, "tofu.exe"), path);
            Assert.True(File.Exists(path));
            Assert.Equal("tofu-ok", File.ReadAllText(path));
            Assert.Equal(path, OpenTofuLocator.Find(dir, searchPathAndWinget: false));
            Assert.True(File.Exists(Path.Combine(dir, OpenTofuLocator.LicenseFileName)));
            Assert.Contains("MPL", File.ReadAllText(Path.Combine(dir, OpenTofuLocator.LicenseFileName)));
            Assert.False(File.Exists(Path.Combine(dir, "tofu-download.zip")));
            Assert.Single(handler.Requests);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Ensure_checksum_mismatch_does_not_write_exe()
    {
        var dir = NewTempDir("tofu-bad");
        try
        {
            var zip = ZipWithFile("tofu.exe", "evil"u8.ToArray());
            var pin = new OpenTofuDownloadPin(
                "test",
                "https://example.test/tofu.zip",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            var handler = new MapHandler();
            handler.Map(pin.ZipUrl, zip);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => OpenTofuLocator.EnsureAsync(http, dir, pin));

            Assert.Contains("SHA-256", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(dir, "tofu.exe")));
            Assert.False(File.Exists(Path.Combine(dir, "tofu.exe.new")));
            Assert.False(File.Exists(Path.Combine(dir, "tofu-download.zip")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Ensure_does_not_install_terraform_exe_from_zip()
    {
        var dir = NewTempDir("tofu-tf");
        try
        {
            var zip = ZipWithFile("terraform.exe", "nope"u8.ToArray());
            var pin = new OpenTofuDownloadPin(
                "test",
                "https://example.test/tofu.zip",
                Sha256Hex(zip));
            var handler = new MapHandler();
            handler.Map(pin.ZipUrl, zip);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => OpenTofuLocator.EnsureAsync(http, dir, pin));

            Assert.Contains("tofu.exe", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(dir, "tofu.exe")));
            Assert.False(File.Exists(Path.Combine(dir, "terraform.exe")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Ensure_refuses_hashicorp_terraform_url()
    {
        var dir = NewTempDir("tofu-url");
        try
        {
            var pin = new OpenTofuDownloadPin(
                "test",
                "https://releases.hashicorp.com/terraform/1.5.0/terraform_1.5.0_windows_amd64.zip",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
            var handler = new FailHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => OpenTofuLocator.EnsureAsync(http, dir, pin));

            Assert.Contains("OpenTofu", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(Path.Combine(dir, "tofu.exe")));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Ensure_skips_http_when_bundled_exe_exists()
    {
        var dir = NewTempDir("tofu-skip");
        try
        {
            var exe = Path.Combine(dir, "tofu.exe");
            File.WriteAllText(exe, "already");
            var handler = new FailHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

            var path = await OpenTofuLocator.EnsureAsync(
                http,
                dir,
                OpenTofuDownloadPin.Product);

            Assert.Equal(exe, path);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static byte[] ZipWithFile(string name, byte[] contents)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(contents);
        }

        return ms.ToArray();
    }

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-" + prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _map = new(StringComparer.Ordinal);

        public List<Uri> Requests { get; } = [];

        public void Map(string url, byte[] body) => _map[url] = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent([]),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }

    private sealed class FailHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            throw new InvalidOperationException("HTTP should not run when tofu.exe already exists.");
        }
    }
}
