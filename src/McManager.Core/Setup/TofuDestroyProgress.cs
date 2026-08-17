using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>Parses OpenTofu destroy stdout for UI percent. Does not call OCI.</summary>
public sealed class TofuDestroyProgress
{
    private static readonly Regex PlanToDestroy = new(
        @"Plan:\s+\d+\s+to add,\s+\d+\s+to change,\s+(\d+)\s+to destroy",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DestroySummary = new(
        @"Destroy complete! Resources:\s+(\d+)\s+destroyed",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public int ToDestroy { get; private set; }

    public int Destroyed { get; private set; }

    public int PercentOfDestroyPhase
    {
        get
        {
            if (ToDestroy <= 0)
                return Destroyed > 0 ? 100 : 0;
            return Math.Clamp((int)Math.Round(100.0 * Destroyed / ToDestroy), 0, 100);
        }
    }

    public void Observe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var plan = PlanToDestroy.Match(line);
        if (plan.Success && int.TryParse(plan.Groups[1].Value, out var n) && n >= 0)
            ToDestroy = n;

        var summary = DestroySummary.Match(line);
        if (summary.Success && int.TryParse(summary.Groups[1].Value, out var done) && done >= 0)
        {
            Destroyed = Math.Max(Destroyed, done);
            return;
        }

        if (line.Contains("Destruction complete", StringComparison.OrdinalIgnoreCase))
            Destroyed++;
    }
}
