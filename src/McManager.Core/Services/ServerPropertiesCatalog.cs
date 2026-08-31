namespace McManager.Core.Services;

/// <summary>
/// Curated <c>server.properties</c> keys the Manager may edit.
/// MOTD stays on the identity path. Product-owned keys are never written.
/// </summary>
public static class ServerPropertiesCatalog
{
    public const string Difficulty = "difficulty";
    public const string Gamemode = "gamemode";
    public const string MaxPlayers = "max-players";
    public const string Pvp = "pvp";
    public const string SpawnProtection = "spawn-protection";
    public const string ViewDistance = "view-distance";
    public const string SimulationDistance = "simulation-distance";
    public const string Hardcore = "hardcore";
    public const string ForceGamemode = "force-gamemode";
    public const string AllowFlight = "allow-flight";

    public const int MaxPlayersMin = 1;
    public const int MaxPlayersMax = 200;
    public const int DistanceMin = 3;
    public const int DistanceMax = 32;
    public const int SpawnProtectionMin = 0;
    public const int SpawnProtectionMax = 256;

    public static readonly IReadOnlyList<string> Difficulties =
        ["peaceful", "easy", "normal", "hard"];

    public static readonly IReadOnlyList<string> Gamemodes =
        ["survival", "creative", "adventure", "spectator"];

    /// <summary>Keys shown on every supported Minecraft version (1.12.2+).</summary>
    public static readonly IReadOnlyList<string> AlwaysVisible =
    [
        Difficulty,
        Gamemode,
        MaxPlayers,
        SpawnProtection,
        ViewDistance,
        Hardcore,
        ForceGamemode,
        AllowFlight,
    ];

    public static readonly HashSet<string> Curated = new(StringComparer.Ordinal)
    {
        Difficulty,
        Gamemode,
        MaxPlayers,
        Pvp,
        SpawnProtection,
        ViewDistance,
        SimulationDistance,
        Hardcore,
        ForceGamemode,
        AllowFlight,
    };

    public static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        "enable-rcon",
        "rcon.password",
        "rcon.port",
        "server-port",
        "server-ip",
        "query.port",
        "enable-query",
        "white-list",
        "enforce-whitelist",
        "online-mode",
        "motd",
        "level-name",
        "management-server-secret",
        "management-server-enabled",
        "management-server-host",
        "management-server-port",
        "management-server-tls-keystore-password",
    };

    /// <summary>Bootstrap / first-Save seeds. Matches on-box <c>if_missing</c> for difficulty and max-players.</summary>
    public static readonly IReadOnlyDictionary<string, string> ProductDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Difficulty] = "normal",
            [Gamemode] = "survival",
            [MaxPlayers] = "20",
            [Pvp] = "true",
            [SpawnProtection] = "16",
            [ViewDistance] = "10",
            [SimulationDistance] = "10",
            [Hardcore] = "false",
            [ForceGamemode] = "false",
            [AllowFlight] = "false",
        };

    public static bool SupportsSimulationDistance(string? minecraftVersion) =>
        MinecraftRelease.TryParse(minecraftVersion, out var release)
        && release.IsAtLeast(1, 18, 0);

    /// <summary>
    /// Vanilla dropped the <c>pvp</c> property in 1.21.9 (gamerule instead).
    /// Calendar versions (26.x) are after that cut.
    /// </summary>
    public static bool SupportsPvpProperty(string? minecraftVersion) =>
        MinecraftRelease.TryParse(minecraftVersion, out var release)
        && release.IsBefore(1, 21, 9);

    public static IReadOnlyList<string> VisibleKeys(string? minecraftVersion)
    {
        var keys = new List<string>(AlwaysVisible.Count + 2);
        keys.AddRange(AlwaysVisible);
        if (SupportsPvpProperty(minecraftVersion))
            keys.Add(Pvp);
        if (SupportsSimulationDistance(minecraftVersion))
            keys.Add(SimulationDistance);
        return keys;
    }

    /// <summary>
    /// Keep allowlisted keys for this Minecraft version, normalize values, reject product-owned keys.
    /// </summary>
    public static ServiceResult<Dictionary<string, string>> Sanitize(
        IReadOnlyDictionary<string, string>? source,
        string? minecraftVersion)
    {
        if (source is not null)
        {
            foreach (var key in source.Keys)
            {
                if (Forbidden.Contains(key))
                    return ServiceResult<Dictionary<string, string>>.Fail(
                        $"Cannot edit {key} from Manager.");
            }
        }

        var visible = VisibleKeys(minecraftVersion);
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in visible)
        {
            var raw = "";
            if (source is not null)
                source.TryGetValue(key, out raw);
            var normalized = NormalizeValue(key, raw, out var error);
            if (error is not null)
                return ServiceResult<Dictionary<string, string>>.Fail(error);
            output[key] = normalized!;
        }

        if (output.TryGetValue(ViewDistance, out var viewRaw)
            && output.TryGetValue(SimulationDistance, out var simRaw)
            && int.TryParse(viewRaw, out var view)
            && int.TryParse(simRaw, out var sim)
            && sim > view)
        {
            output[SimulationDistance] = viewRaw;
        }

        return ServiceResult<Dictionary<string, string>>.Ok(output);
    }

    public static string DefaultFor(string key) =>
        ProductDefaults.TryGetValue(key, out var value) ? value : "";

    internal static string? NormalizeValue(string key, string? raw, out string? error)
    {
        error = null;
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
            text = DefaultFor(key);

        switch (key)
        {
            case Difficulty:
                text = CanonicalEnum(text, Difficulties, "0", "1", "2", "3");
                if (text is null)
                {
                    error = "Difficulty must be peaceful, easy, normal, or hard.";
                    return null;
                }

                return text;
            case Gamemode:
                text = CanonicalEnum(text, Gamemodes, "0", "1", "2", "3");
                if (text is null)
                {
                    error = "Game mode must be survival, creative, adventure, or spectator.";
                    return null;
                }

                return text;
            case Pvp:
            case Hardcore:
            case ForceGamemode:
            case AllowFlight:
                if (!TryCanonicalBool(text, out var flag))
                {
                    error = $"{key} must be true or false.";
                    return null;
                }

                return flag ? "true" : "false";
            case MaxPlayers:
                if (!TryClampInt(text, MaxPlayersMin, MaxPlayersMax, out var maxPlayers))
                {
                    error = $"Max players must be {MaxPlayersMin}–{MaxPlayersMax}.";
                    return null;
                }

                return maxPlayers.ToString();
            case SpawnProtection:
                if (!TryClampInt(text, SpawnProtectionMin, SpawnProtectionMax, out var spawn))
                {
                    error = $"Spawn protection must be {SpawnProtectionMin}–{SpawnProtectionMax}.";
                    return null;
                }

                return spawn.ToString();
            case ViewDistance:
            case SimulationDistance:
                if (!TryClampInt(text, DistanceMin, DistanceMax, out var distance))
                {
                    error = $"{key} must be {DistanceMin}–{DistanceMax}.";
                    return null;
                }

                return distance.ToString();
            default:
                error = $"Unknown setting {key}.";
                return null;
        }
    }

    private static string? CanonicalEnum(
        string text,
        IReadOnlyList<string> names,
        params string[] legacyNumbers)
    {
        if (legacyNumbers.Length == names.Count
            && int.TryParse(text, out var n)
            && n >= 0
            && n < names.Count)
            return names[n];

        foreach (var name in names)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }

    private static bool TryCanonicalBool(string text, out bool value)
    {
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            || text == "1")
        {
            value = true;
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)
            || text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryClampInt(string text, int min, int max, out int value)
    {
        value = 0;
        if (!int.TryParse(text, out var parsed))
            return false;
        if (parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }
}

/// <summary>Best-effort parse of a Minecraft release id for property gating.</summary>
internal readonly record struct MinecraftRelease(int Major, int Minor, int Patch, bool IsCalendar)
{
    public static bool TryParse(string? minecraftVersion, out MinecraftRelease release)
    {
        release = default;
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return false;

        var id = minecraftVersion.Trim();
        var dash = id.IndexOf('-');
        if (dash > 0)
            id = id[..dash];

        var parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major))
            return false;
        if (!int.TryParse(StripNonDigitSuffix(parts[1]), out var minor))
            return false;
        var patch = 0;
        if (parts.Length >= 3)
            int.TryParse(StripNonDigitSuffix(parts[2]), out patch);

        if (major >= 25 && !id.StartsWith("1.", StringComparison.Ordinal))
        {
            release = new MinecraftRelease(major, minor, patch, IsCalendar: true);
            return true;
        }

        if (major != 1)
            return false;

        release = new MinecraftRelease(major, minor, patch, IsCalendar: false);
        return true;
    }

    public bool IsAtLeast(int major, int minor, int patch)
    {
        if (IsCalendar)
            return true;
        if (Major != major)
            return Major > major;
        if (Minor != minor)
            return Minor > minor;
        return Patch >= patch;
    }

    public bool IsBefore(int major, int minor, int patch)
    {
        if (IsCalendar)
            return false;
        if (Major != major)
            return Major < major;
        if (Minor != minor)
            return Minor < minor;
        return Patch < patch;
    }

    private static string StripNonDigitSuffix(string value)
    {
        var i = 0;
        while (i < value.Length && char.IsDigit(value[i]))
            i++;
        return i == 0 ? value : value[..i];
    }
}
