namespace McManager.Core.Services;

/// <summary>
/// Browser URL for the player 2D map. Door <b>ephemeral</b> IPv4 on TCP 80
/// (or 8081 only if that bind fallback is in use). Never the reserved play IP
/// and never admin <c>:8080</c>.
/// </summary>
public static class PlayerMapPublicUrl
{
    public const int DefaultPort = SecurityListIngressPlanner.PlayerHttpPort;

    public const int FallbackPort = 8081;

    public const int AdminHttpPort = 8080;

    /// <summary>
    /// <c>http://{door ephemeral}/</c> on port 80; <c>http://{host}:{port}/</c>
    /// otherwise. Null when the host is missing.
    /// </summary>
    public static string? TryFormat(string? doorEphemeralHost, int port = DefaultPort)
    {
        if (string.IsNullOrWhiteSpace(doorEphemeralHost) || port is <= 0 or > 65535)
            return null;

        var host = doorEphemeralHost.Trim();
        if (host.Contains("://", StringComparison.Ordinal))
            return null;

        return port == DefaultPort
            ? $"http://{host}/"
            : $"http://{host}:{port}/";
    }
}
