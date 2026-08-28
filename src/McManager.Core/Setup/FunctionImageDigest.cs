namespace McManager.Core.Setup;

/// <summary>Compare a bundled Function image digest to the live OCIR tag.</summary>
public static class FunctionImageDigest
{
    public static string Normalize(string? digest)
    {
        var t = (digest ?? "").Trim();
        if (t.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            t = t["sha256:".Length..];
        return t.ToLowerInvariant();
    }

    /// <summary>
    /// Copy when the bundled tar has a digest and the live tag is missing or different.
    /// Blank bundled digest means this is not the tar path (caller uses docker buildx).
    /// </summary>
    public static bool NeedsCopy(string? bundledDigest, string? liveDigest)
    {
        if (string.IsNullOrWhiteSpace(bundledDigest))
            return false;
        if (string.IsNullOrWhiteSpace(liveDigest))
            return true;
        return !string.Equals(Normalize(bundledDigest), Normalize(liveDigest), StringComparison.Ordinal);
    }
}
