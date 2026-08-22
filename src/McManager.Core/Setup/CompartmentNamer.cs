using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>
/// Product compartment display names: <c>mcmgr</c>, then <c>mcmgr-2</c>, <c>mcmgr-3</c>, …
/// Hyphen suffix (OCI allows letters, numbers, periods, hyphens, underscores).
/// </summary>
public static partial class CompartmentNamer
{
    public const string BaseName = "mcmgr";
    public const int MaxNumericSuffix = 99;

    /// <summary>
    /// First unused name among <see cref="BaseName"/> and <c>mcmgr-2</c>…<c>mcmgr-99</c>.
    /// Comparison is case-insensitive (OCI compartment names are unique ignoring case).
    /// </summary>
    public static string NextAvailable(IEnumerable<string?> existingNames)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existingNames is not null)
        {
            foreach (var raw in existingNames)
            {
                if (!string.IsNullOrWhiteSpace(raw))
                    taken.Add(raw.Trim());
            }
        }

        if (!taken.Contains(BaseName))
            return BaseName;

        for (var n = 2; n <= MaxNumericSuffix; n++)
        {
            var candidate = $"{BaseName}-{n}";
            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException(
            $"Could not find a free compartment name ({BaseName} through {BaseName}-{MaxNumericSuffix} are taken).");
    }

    public static bool TryNextAvailable(IEnumerable<string?> existingNames, out string name)
    {
        try
        {
            name = NextAvailable(existingNames);
            return true;
        }
        catch (InvalidOperationException)
        {
            name = BaseName;
            return false;
        }
    }

    /// <summary><c>mcmgr</c> or <c>mcmgr-N</c> with N ≥ 1.</summary>
    public static bool IsProductName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;
        return ProductNamePattern().IsMatch(displayName.Trim());
    }

    /// <summary>
    /// Keep the assigned name on Deploy retry when OpenTofu state already exists
    /// (or apply already passed tofu) so we do not create mcmgr-2 beside our own mcmgr.
    /// </summary>
    public static bool ShouldReuseAssignedName(
        string? compartmentName,
        string? applyStage,
        bool hasLocalTofuState)
    {
        if (string.IsNullOrWhiteSpace(compartmentName))
            return false;
        if (hasLocalTofuState)
            return true;
        return SetupApplyStage.Reached(applyStage, SetupApplyStage.TofuApplied);
    }

    [GeneratedRegex(@"^mcmgr(?:-[1-9][0-9]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductNamePattern();
}
