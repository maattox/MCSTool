using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>Docker Registry HTTP API v2 push into OCIR. No Docker daemon.</summary>
public static class OcirRegistryPusher
{
    public static async Task<ServiceResult> PushAsync(
        string registryHost,
        string repository,
        string tag,
        string username,
        string password,
        PreparedFunctionImage image,
        IProgress<string>? log,
        HttpMessageHandler? handler = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registryHost) || string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(tag))
            return ServiceResult.Fail("OCIR host, repository, or tag is missing.");
        if (image.Blobs.Count == 0)
            return ServiceResult.Fail("Prepared Function image has no blobs.");

        var ownsHandler = handler is null;
        handler ??= new HttpClientHandler();
        using var client = new HttpClient(handler, disposeHandler: ownsHandler)
        {
            Timeout = TimeSpan.FromMinutes(15),
            BaseAddress = new Uri("https://" + registryHost.TrimEnd('/') + "/"),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McManager-Setup");

        var auth = await AuthenticateAsync(
            client,
            repository,
            username,
            password,
            log,
            cancellationToken).ConfigureAwait(false);
        if (!auth.Succeeded)
            return ServiceResult.Fail(auth.Error ?? "OCIR login failed.");

        foreach (var blob in image.Blobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uploaded = await EnsureBlobAsync(
                client,
                repository,
                blob,
                log,
                cancellationToken).ConfigureAwait(false);
            if (!uploaded.Succeeded)
                return uploaded;
        }

        log?.Report($"Putting OCIR manifest {repository}:{tag}…");
        using var put = new HttpRequestMessage(HttpMethod.Put, $"v2/{repository}/manifests/{tag}");
        put.Content = new ByteArrayContent(image.ManifestJson);
        put.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(image.ManifestMediaType);
        using var putResp = await client.SendAsync(put, cancellationToken).ConfigureAwait(false);
        if (putResp.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            var body = await putResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult.Fail($"OCIR manifest PUT failed ({(int)putResp.StatusCode}): {TrimBody(body)}");
        }

        log?.Report("Copied Function image into OCIR (no Docker).");
        return ServiceResult.Ok();
    }

    internal static async Task<ServiceResult<string?>> AuthenticateAsync(
        HttpClient client,
        string repository,
        string username,
        string password,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        using var probe = new HttpRequestMessage(HttpMethod.Get, "v2/");
        probe.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var probeResp = await client.SendAsync(probe, cancellationToken).ConfigureAwait(false);
        if (probeResp.IsSuccessStatusCode)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            return ServiceResult<string?>.Ok(null);
        }

        if (probeResp.StatusCode != HttpStatusCode.Unauthorized)
        {
            var body = await probeResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult<string?>.Fail($"OCIR /v2/ failed ({(int)probeResp.StatusCode}): {TrimBody(body)}");
        }

        var challenge = probeResp.Headers.WwwAuthenticate.FirstOrDefault()?.ToString()
            ?? (probeResp.Headers.TryGetValues("WWW-Authenticate", out var raw) ? raw.FirstOrDefault() : null);
        if (string.IsNullOrWhiteSpace(challenge) || !challenge.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
            return ServiceResult<string?>.Ok(null);
        }

        if (!TryParseBearerChallenge(challenge, out var realm, out var service))
            return ServiceResult<string?>.Fail("OCIR WWW-Authenticate Bearer challenge was not parseable.");

        var scope = "repository:" + repository + ":pull,push";
        var tokenUri = AppendQuery(realm, "service=" + Uri.EscapeDataString(service) + "&scope=" + Uri.EscapeDataString(scope));
        using var tokenReq = new HttpRequestMessage(HttpMethod.Get, tokenUri);
        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        log?.Report("Requesting OCIR registry token…");
        using var tokenResp = await client.SendAsync(tokenReq, cancellationToken).ConfigureAwait(false);
        var tokenBody = await tokenResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!tokenResp.IsSuccessStatusCode)
            return ServiceResult<string?>.Fail($"OCIR token request failed ({(int)tokenResp.StatusCode}): {TrimBody(tokenBody)}");

        string? token = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(tokenBody) ? "{}" : tokenBody);
            if (doc.RootElement.TryGetProperty("token", out var t))
                token = t.GetString();
            else if (doc.RootElement.TryGetProperty("access_token", out var a))
                token = a.GetString();
        }
        catch (JsonException ex)
        {
            return ServiceResult<string?>.Fail("OCIR token JSON was invalid: " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(token))
            return ServiceResult<string?>.Fail("OCIR token response had no token.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return ServiceResult<string?>.Ok(token);
    }

    internal static bool TryParseBearerChallenge(string header, out string realm, out string service)
    {
        realm = "";
        service = "";
        var realmMatch = Regex.Match(header, @"realm=""([^""]+)""", RegexOptions.IgnoreCase);
        var serviceMatch = Regex.Match(header, @"service=""([^""]+)""", RegexOptions.IgnoreCase);
        if (!realmMatch.Success)
            return false;
        realm = realmMatch.Groups[1].Value;
        service = serviceMatch.Success ? serviceMatch.Groups[1].Value : "";
        return true;
    }

    private static async Task<ServiceResult> EnsureBlobAsync(
        HttpClient client,
        string repository,
        PreparedFunctionBlob blob,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, $"v2/{repository}/blobs/{blob.Digest}");
        using var headResp = await client.SendAsync(head, cancellationToken).ConfigureAwait(false);
        if (headResp.StatusCode == HttpStatusCode.OK)
        {
            log?.Report($"OCIR blob already present {ShortDigest(blob.Digest)}.");
            return ServiceResult.Ok();
        }

        log?.Report($"Uploading OCIR blob {ShortDigest(blob.Digest)} ({blob.Size} bytes)…");
        using var start = new HttpRequestMessage(HttpMethod.Post, $"v2/{repository}/blobs/uploads/");
        using var startResp = await client.SendAsync(start, cancellationToken).ConfigureAwait(false);
        if (startResp.StatusCode != HttpStatusCode.Accepted)
        {
            var body = await startResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult.Fail($"OCIR blob upload start failed ({(int)startResp.StatusCode}): {TrimBody(body)}");
        }

        var location = startResp.Headers.Location?.ToString()
            ?? (startResp.Headers.TryGetValues("Location", out var locs) ? locs.FirstOrDefault() : null);
        if (string.IsNullOrWhiteSpace(location))
            return ServiceResult.Fail("OCIR blob upload did not return Location.");

        var uploadUri = AppendQuery(ToClientUri(client, location), "digest=" + blob.Digest);
        using var file = File.OpenRead(blob.FilePath);
        using var put = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        put.Content = new StreamContent(file);
        put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        put.Content.Headers.ContentLength = blob.Size;
        using var putResp = await client.SendAsync(put, cancellationToken).ConfigureAwait(false);
        if (putResp.StatusCode is not HttpStatusCode.Created and not HttpStatusCode.OK)
        {
            var body = await putResp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult.Fail($"OCIR blob PUT failed ({(int)putResp.StatusCode}): {TrimBody(body)}");
        }

        return ServiceResult.Ok();
    }

    private static Uri ToClientUri(HttpClient client, string location)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var abs))
            return abs;
        if (client.BaseAddress is null)
            return new Uri(location, UriKind.Relative);
        return new Uri(client.BaseAddress, location);
    }

    private static string AppendQuery(Uri uri, string query) =>
        AppendQuery(uri.ToString(), query);

    private static string AppendQuery(string uri, string query)
    {
        var sep = uri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return uri + sep + query;
    }

    private static string ShortDigest(string digest)
    {
        var hex = digest.Contains(':', StringComparison.Ordinal)
            ? digest[(digest.IndexOf(':') + 1)..]
            : digest;
        return hex.Length <= 12 ? hex : hex[..12];
    }

    private static string TrimBody(string body)
    {
        var t = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length <= 400 ? t : t[..400];
    }
}
