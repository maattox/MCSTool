using System.Text;
using System.Text.Json;

namespace McManager.Core.Services;

public interface IDoorClient
{
    Task<ServiceResult<string>> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult<DoorStatus>> GetStatusParsedAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> WakeAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> IdleEmptyAsync(CancellationToken cancellationToken = default);
}

public sealed class DoorClient : IDoorClient, IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public DoorClient(string doorAdminBaseUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(doorAdminBaseUrl))
            throw new ArgumentException("Door admin base URL is empty.", nameof(doorAdminBaseUrl));

        _http = httpClient ?? new HttpClient { Timeout = DefaultTimeout };
        _http.BaseAddress = new Uri(doorAdminBaseUrl.TrimEnd('/') + "/");
    }

    public async Task<ServiceResult<string>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("api/status", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ServiceResult<string>.Fail(FormatHttpError("GET /api/status", response.StatusCode, body));

            return ServiceResult<string>.Ok(body);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(FormatTransportError("GET /api/status", ex));
        }
    }

    public async Task<ServiceResult<DoorStatus>> GetStatusParsedAsync(CancellationToken cancellationToken = default)
    {
        var raw = await GetStatusAsync(cancellationToken);
        if (!raw.Succeeded || raw.Value is null)
            return ServiceResult<DoorStatus>.Fail(raw.Error ?? "Door status failed.");

        try
        {
            var status = JsonSerializer.Deserialize<DoorStatus>(raw.Value, JsonOptions);
            if (status is null)
                return ServiceResult<DoorStatus>.Fail("Door status JSON deserialized to null.");

            return ServiceResult<DoorStatus>.Ok(status);
        }
        catch (Exception ex)
        {
            return ServiceResult<DoorStatus>.Fail($"Failed to parse door status: {ex.Message}");
        }
    }

    public async Task<ServiceResult> WakeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("api/wake", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted
                || response.IsSuccessStatusCode)
            {
                return ServiceResult.Ok();
            }

            return ServiceResult.Fail(FormatHttpError("POST /api/wake", response.StatusCode, body));
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(FormatTransportError("POST /api/wake", ex));
        }
    }

    public async Task<ServiceResult> IdleEmptyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("api/idle-empty", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return ServiceResult.Ok();

            return ServiceResult.Fail(FormatHttpError("POST /api/idle-empty", response.StatusCode, body));
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail(FormatTransportError("POST /api/idle-empty", ex));
        }
    }

    private static string FormatHttpError(string operation, System.Net.HttpStatusCode statusCode, string body)
    {
        var snippet = body.Length > 200 ? body[..200] + "…" : body;
        return $"{operation} returned {(int)statusCode}: {snippet}";
    }

    private static string FormatTransportError(string operation, Exception ex) =>
        $"{operation} failed: {ex.Message}";

    public void Dispose() => _http.Dispose();
}
