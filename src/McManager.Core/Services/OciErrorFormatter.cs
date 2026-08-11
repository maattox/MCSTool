using System.Reflection;
using System.Text;

namespace McManager.Core.Services;

public static class OciErrorFormatter
{
    public static string Format(string operation, Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(operation).Append(" failed: ").Append(ex.Message);

        if (IsRateLimit(ex))
            sb.Append(" (OCI rate limit — try again shortly.)");

        var requestId = TryGetOpcRequestId(ex);
        if (!string.IsNullOrWhiteSpace(requestId))
            sb.Append(" [opc-request-id: ").Append(requestId).Append(']');

        return sb.ToString();
    }

    public static bool IsRateLimit(Exception ex)
    {
        var status = TryGetStatusCode(ex);
        if (status == 429)
            return true;

        var code = TryGetErrorCode(ex);
        return string.Equals(code, "TooManyRequests", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("TooManyRequests", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("User-rate limit", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNotFound(Exception ex)
    {
        var status = TryGetStatusCode(ex);
        if (status == 404)
            return true;

        var code = TryGetErrorCode(ex);
        return string.Equals(code, "ObjectNotFound", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "NamespaceNotFound", StringComparison.OrdinalIgnoreCase)
               || string.Equals(code, "BucketNotFound", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("ObjectNotFound", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("NotAuthorizedOrNotFound", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNotFoundMessage(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("ObjectNotFound", StringComparison.OrdinalIgnoreCase)
            || error.Contains("NotAuthorizedOrNotFound", StringComparison.OrdinalIgnoreCase)
            || error.Contains("404", StringComparison.OrdinalIgnoreCase));

    private static string? TryGetOpcRequestId(Exception ex)
    {
        foreach (var name in new[] { "OpcRequestId", "opcRequestId", "RequestId" })
        {
            var prop = ex.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop?.GetValue(ex) is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        // Walk inner exceptions (SDK sometimes wraps).
        return ex.InnerException is null ? null : TryGetOpcRequestId(ex.InnerException);
    }

    private static int? TryGetStatusCode(Exception ex)
    {
        foreach (var name in new[] { "StatusCode", "HttpStatusCode" })
        {
            var prop = ex.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop is null)
                continue;

            var value = prop.GetValue(ex);
            if (value is int i)
                return i;
            if (value is Enum e)
                return Convert.ToInt32(e);
        }

        return ex.InnerException is null ? null : TryGetStatusCode(ex.InnerException);
    }

    private static string? TryGetErrorCode(Exception ex)
    {
        var prop = ex.GetType().GetProperty("ServiceCode", BindingFlags.Public | BindingFlags.Instance)
                   ?? ex.GetType().GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
        if (prop?.GetValue(ex) is string s && !string.IsNullOrWhiteSpace(s))
            return s;

        return ex.InnerException is null ? null : TryGetErrorCode(ex.InnerException);
    }
}
