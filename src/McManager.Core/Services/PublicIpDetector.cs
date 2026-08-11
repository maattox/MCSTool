using System.Net.Http;

namespace McManager.Core.Services;

public static class PublicIpDetector
{
    private static readonly string[] Endpoints =
    [
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
    ];

    public static async Task<ServiceResult<string>> FetchPublicIpAsync(
        CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        foreach (var endpoint in Endpoints)
        {
            try
            {
                var body = (await http.GetStringAsync(endpoint, cancellationToken)).Trim();
                if (McManager.Core.Config.FriendRules.TryNormalizeIp(body, out var ip))
                    return ServiceResult<string>.Ok(ip);
            }
            catch
            {
                // try next endpoint
            }
        }

        return ServiceResult<string>.Fail("Could not detect public IP from ipify/ifconfig.me/icanhazip.");
    }
}
