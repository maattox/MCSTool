namespace McManager.Core.Services;

/// <summary>One name from RCON <c>list</c> / <c>list uuids</c>.</summary>
public sealed record OnlinePlayer(string Name, string Uuid, string UuidHyphenless)
{
    public bool HasUuid => UuidHyphenless.Length == 32;

    public static OnlinePlayer Create(string name, string? uuid)
    {
        var trimmedName = (name ?? "").Trim();
        var rawUuid = (uuid ?? "").Trim();
        return new OnlinePlayer(
            trimmedName,
            rawUuid,
            MinecraftConsoleRemote.ToHyphenlessUuid(rawUuid));
    }
}
