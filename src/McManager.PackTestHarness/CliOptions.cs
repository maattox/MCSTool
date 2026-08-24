namespace McManager.PackTestHarness;

internal sealed class CliOptions
{
    public required string PackId { get; init; }
    public required string CatalogPath { get; init; }
    public required string PhaseDir { get; init; }
    public bool AnalyzeOnly { get; init; }
    public bool WipeWorld { get; init; } = true;

    public static bool TryParse(string[] args, out CliOptions? options, out string? error)
    {
        options = null;
        error = null;
        string? packId = null;
        string? catalog = null;
        string? phase = null;
        var analyzeOnly = false;
        var wipeWorld = true;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-h" or "--help")
            {
                error = HelpText;
                return false;
            }

            if (a == "--pack")
            {
                if (!TryValue(args, ref i, out packId, out error, "--pack"))
                    return false;
                continue;
            }

            if (a == "--catalog")
            {
                if (!TryValue(args, ref i, out catalog, out error, "--catalog"))
                    return false;
                continue;
            }

            if (a == "--phase")
            {
                if (!TryValue(args, ref i, out phase, out error, "--phase"))
                    return false;
                continue;
            }

            if (a == "--analyze-only")
            {
                analyzeOnly = true;
                continue;
            }

            if (a == "--wipe-world")
            {
                wipeWorld = true;
                continue;
            }

            error = $"Unknown argument: {a}\n\n{HelpText}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(packId)
            || string.IsNullOrWhiteSpace(catalog)
            || string.IsNullOrWhiteSpace(phase))
        {
            error = "Required: --pack <id> --catalog <path> --phase <phase-dir>.\n\n" + HelpText;
            return false;
        }

        options = new CliOptions
        {
            PackId = packId.Trim(),
            CatalogPath = Path.GetFullPath(catalog.Trim()),
            PhaseDir = Path.GetFullPath(phase.Trim()),
            AnalyzeOnly = analyzeOnly,
            WipeWorld = wipeWorld,
        };
        return true;
    }

    public const string HelpText =
        """
        McManager.PackTestHarness — headless Change pack (same Core path as Hybrid).

        Usage:
          McManager.PackTestHarness --pack <id> --catalog <path> --phase <phase-dir> [--analyze-only] [--wipe-world]

        Flags:
          --pack <id>         Catalog slug (result filename)
          --catalog <path>    pack-tests/catalog.yaml
          --phase <phase-dir> phases/<id>/ (results/ + logs/ written here)
          --wipe-world        Default on; this suite always wipes
          --analyze-only      Analyze (+ derived zip when Hybrid would); no SSH

        Config:
          MCMANAGER_CONFIG_DIR must be mcmgr-pack-test (TESTING config.local.json).
          Refuses repo data/config.local.json and mcmgr-blank-test.

        Exit:
          0 pass / pass_quarantined
          1 product_fail / blocked_freeze / timeout
          2 infra_fail
          3 usage
        """;

    private static bool TryValue(
        string[] args,
        ref int i,
        out string value,
        out string? error,
        string flag)
    {
        value = "";
        error = null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = $"{flag} needs a value.\n\n{HelpText}";
            return false;
        }

        i++;
        value = args[i];
        return true;
    }
}
