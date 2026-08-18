namespace McManager.Core.Config;

/// <summary>
/// Access mode in <c>friends.local.json</c> and Object Storage <c>ip/mode.json</c>.
/// Missing or unknown values are <see cref="Private"/> — never treat invalid as public.
/// </summary>
public static class IpAccessMode
{
    public const string Private = "private";
    public const string Public = "public";

    public static bool IsPublic(string? mode) =>
        string.Equals(mode?.Trim(), Public, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? mode) =>
        IsPublic(mode) ? Public : Private;
}
