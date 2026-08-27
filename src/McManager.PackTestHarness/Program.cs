using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;

namespace McManager.PackTestHarness;

internal static class Program
{
    public const int ExitUsage = 3;

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help"))
        {
            Console.WriteLine(CliOptions.HelpText);
            return ExitUsage;
        }

        if (!CliOptions.TryParse(args, out var opt, out var parseError) || opt is null)
        {
            Console.Error.WriteLine(parseError);
            return ExitUsage;
        }

        if (!File.Exists(opt.CatalogPath))
        {
            Console.Error.WriteLine("Catalog not found: " + opt.CatalogPath);
            return ExitUsage;
        }

        var result = new PackTestResultDocument
        {
            PackId = opt.PackId,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        var journal = new List<string>();
        IProgress<string> log = new Progress<string>(line =>
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            journal.Add(line.TrimEnd());
            Console.Error.WriteLine(line);
        });

        try
        {
            return await RunAsync(opt, result, journal, log);
        }
        catch (Exception ex)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = YamlText.OneLine(ex.Message);
            result.Notes.Add("unhandled exception");
            return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
        }
    }

    private static async Task<int> RunAsync(
        CliOptions opt,
        PackTestResultDocument result,
        List<string> journal,
        IProgress<string> log)
    {
        var guardError = ConfigGuard.TryAllow(out var dataDirectory, out var config);
        if (guardError is not null || config is null)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = YamlText.OneLine(guardError);
            Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
            return ExitUsage;
        }

        if (!Directory.Exists(opt.PhaseDir))
        {
            Console.Error.WriteLine("Phase directory does not exist: " + opt.PhaseDir);
            return ExitUsage;
        }

        PackCatalog catalog;
        try
        {
            catalog = PackCatalog.Load(opt.CatalogPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Could not parse catalog: " + ex.Message);
            return ExitUsage;
        }

        var row = catalog.Find(opt.PackId);
        if (row is null)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = "Catalog has no pack id '" + opt.PackId + "'.";
            Console.Error.WriteLine(result.FailMessage);
            Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
            return ExitUsage;
        }

        FillExpected(result, row);
        result.Filename = row.Filename;

        var lockError = TryRefuseForeignLock(opt);
        if (lockError is not null)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = lockError;
            return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
        }

        var packsDir = Path.Combine(Path.GetDirectoryName(opt.CatalogPath)!, "packs");
        var packPath = Path.Combine(packsDir, row.Filename);
        if (!File.Exists(packPath))
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = "Pack file not found: " + row.Filename;
            return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
        }

        var actualSha = Layer2LocalOverlay.TryHashFile(packPath) ?? "";
        result.Sha256 = actualSha;
        if (row.HasSha256)
        {
            if (!string.Equals(actualSha, row.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.Verdict = PackVerdict.InfraFail;
                result.FailMessage = "SHA-256 mismatch for " + row.Filename + ".";
                result.Notes.Add("catalog sha256 does not match the file on disk");
                return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
            }
        }
        else if (!opt.AnalyzeOnly)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = "Catalog sha256 is required before a live test.";
            return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
        }

        log.Report("Analyze " + packPath);
        var analysis = SetupPackImport.AnalyzeFile(packPath, dataDirectory: dataDirectory);
        if (!analysis.Succeeded || analysis.Value is null)
        {
            result.Verdict = PackVerdict.ProductFail;
            result.FailMessage = YamlText.OneLine(analysis.Error ?? "Analyze failed.");
            return await FinishAsync(opt, config, result, journal, passLike: false);
        }

        var preview = analysis.Value;
        FillDetected(result, preview);
        result.AutomaticClient = preview.AssistedReview.WillSkip.Count;
        result.UnknownKept = preview.AssistedReview.NeedsYourCall.Count;
        result.Applied.Minecraft = preview.MinecraftVersion;
        result.Applied.Loader = preview.Loader;
        result.Applied.LoaderVersion = preview.LoaderVersion;
        result.Applied.JavaMajor = preview.JavaMajor ?? 0;

        if (!string.IsNullOrWhiteSpace(row.ClientOnlySidecar))
            result.Notes.Add("client-only sidecar not applied (default Keep)");

        if (!PackReplaceUx.FreezeAllowsContinue(preview.FreezeBlockReason) || !preview.CanContinue)
        {
            result.Verdict = PackVerdict.BlockedFreeze;
            result.FailMessage = YamlText.OneLine(
                preview.FreezeBlockReason
                ?? preview.BlockReason
                ?? "Freeze / CanContinue is false.");
            return await FinishAsync(opt, config, result, journal, passLike: false);
        }

        var installPath = packPath;
        if (preview.NeedsIdentityConfirm)
        {
            var javaText = row.JavaMajor > 0
                ? row.JavaMajor.ToString()
                : (preview.JavaMajor?.ToString() ?? "");
            var mc = FirstNonEmpty(row.Minecraft, preview.MinecraftVersion);
            var loader = FirstNonEmpty(row.Loader, preview.Loader);
            var loaderVer = FirstNonEmpty(row.LoaderVersion, preview.LoaderVersion);
            if (!DerivedPackIdentity.IsComplete(mc, loader, loaderVer, javaText))
            {
                result.Verdict = PackVerdict.ProductFail;
                result.FailMessage = DerivedPackIdentity.IdentityIncompleteReason;
                return await FinishAsync(opt, config, result, journal, passLike: false);
            }

            log.Report("Build derived pack (NeedsIdentityConfirm).");
            var build = DerivedPackWorkflow.BuildAndRetain(
                packPath,
                preview.PackName,
                preview.VersionId,
                mc,
                loader,
                loaderVer,
                javaText,
                dataDirectory,
                Path.GetFileName(packPath));
            if (!build.Succeeded || string.IsNullOrWhiteSpace(build.Value))
            {
                result.Verdict = PackVerdict.ProductFail;
                result.FailMessage = YamlText.OneLine(build.Error ?? "Could not build the derived pack.");
                return await FinishAsync(opt, config, result, journal, passLike: false);
            }

            installPath = build.Value;
            result.Applied.Minecraft = mc;
            result.Applied.Loader = loader;
            result.Applied.LoaderVersion = loaderVer;
            _ = int.TryParse(javaText, out var appliedJava);
            result.Applied.JavaMajor = appliedJava;
            result.Notes.Add("derived zip built; original archive untouched");
        }

        if (opt.AnalyzeOnly)
        {
            result.Verdict = PackVerdict.Pass;
            result.Notes.Add("analyze-only; ReplacePackAsync skipped");
            return Finish(opt, result, journal, ReadyGate.AnalyzeOnlySkipped());
        }

        log.Report("Prepare VM1: stop minecraft, disable idle (OS-ISSUE-7)");
        var prep = await ReadyGate.RunLiveAsync(config, passLike: false, CancellationToken.None);
        foreach (var n in prep.Notes)
        {
            if (!result.Notes.Contains(n, StringComparer.Ordinal))
                result.Notes.Add(n);
        }

        if (!prep.ReadyForNext)
        {
            result.Verdict = PackVerdict.InfraFail;
            result.FailMessage = YamlText.OneLine(
                prep.Notes.Count > 0
                    ? string.Join(" ", prep.Notes)
                    : "VM1 not ready for replace (SSH, lifecycle, idle, or minecraft still up).");
            result.Ssh = prep.Ssh;
            result.Vm1 = prep.Vm1;
            result.MinecraftUnit = prep.MinecraftUnit;
            result.IdleDisabled = prep.IdleDisabled;
            return Finish(opt, result, journal, prep);
        }

        result.Notes.Add("stopped minecraft and disabled idle before replace (OS-ISSUE-7)");
        log.Report("ReplacePackAsync wipe_world=true (idle hold during start)");
        using var holdCts = new CancellationTokenSource();
        var holdTask = IdleHold.HoldUntilCancelledAsync(config, log, holdCts.Token);
        ServiceResult<PackReplaceResult> replace;
        try
        {
            var bootstrap = new SetupBootstrapService();
            replace = await bootstrap.ReplacePackAsync(
                config.Vm1,
                new PackReplaceRequest(installPath, wipeWorld: opt.WipeWorld, dataDirectory),
                log);
        }
        finally
        {
            holdCts.Cancel();
            try
            {
                await holdTask;
            }
            catch (OperationCanceledException)
            {
                // expected when the hold loop is cancelled
            }

            try
            {
                await IdleHold.DisableOnceAsync(config, CancellationToken.None);
            }
            catch (Exception ex)
            {
                result.Notes.Add("Idle disable after replace failed: " + YamlText.OneLine(ex.Message));
            }
        }

        result.Notes.Add("held idle disabled during replace (OS-ISSUE-7 record_boot re-enable)");

        if (!replace.Succeeded || replace.Value is null)
        {
            var err = replace.Error ?? "Pack replace failed.";
            result.Verdict = FailClassifier.FromReplaceError(err);
            result.FailMessage = YamlText.OneLine(err);
            result.CrashLoop = err.Contains("keep restarting", StringComparison.OrdinalIgnoreCase)
                || err.Contains("crash", StringComparison.OrdinalIgnoreCase);
            result.Fatal = MinecraftReadiness.HasFatalJournal(err);
            result.Quarantine = err.Contains("quarantine", StringComparison.OrdinalIgnoreCase)
                || err.Contains("Layer 3", StringComparison.OrdinalIgnoreCase);
            await TryAppendRemoteJournalAsync(config, journal);
            return await FinishAsync(opt, config, result, journal, passLike: false);
        }

        var packResult = replace.Value;
        result.Quarantine = !string.IsNullOrWhiteSpace(packResult.QuarantineNotice);
        result.RconList = true;
        result.Verdict = result.Quarantine ? PackVerdict.PassQuarantined : PackVerdict.Pass;
        if (result.Quarantine)
            result.Notes.Add(YamlText.OneLine(packResult.QuarantineNotice));
        await TryAppendRemoteJournalAsync(config, journal);
        return await FinishAsync(opt, config, result, journal, passLike: true);
    }

    private static async Task TryAppendRemoteJournalAsync(ManagerLocalConfig config, List<string> journal)
    {
        try
        {
            var ssh = new SshService();
            var logs = await ssh.FetchMinecraftLogsAsync(config.Vm1, MinecraftReadiness.ProbeJournalLines);
            if (logs.Succeeded && !string.IsNullOrWhiteSpace(logs.Output))
            {
                foreach (var line in logs.Output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                    journal.Add(line);
            }
        }
        catch (Exception ex)
        {
            journal.Add("journal fetch failed: " + ex.Message);
        }
    }

    private static async Task<int> FinishAsync(
        CliOptions opt,
        ManagerLocalConfig config,
        PackTestResultDocument result,
        List<string> journal,
        bool passLike)
    {
        ReadyGateReport gate;
        if (opt.AnalyzeOnly)
            gate = ReadyGate.AnalyzeOnlySkipped();
        else
            gate = await ReadyGate.RunLiveAsync(config, passLike, CancellationToken.None);
        return Finish(opt, result, journal, gate);
    }

    private static int Finish(
        CliOptions opt,
        PackTestResultDocument result,
        List<string> journal,
        ReadyGateReport gate)
    {
        result.FinishedUtc = DateTimeOffset.UtcNow;
        result.ReadyForNext = gate.ReadyForNext;
        result.Ssh = gate.Ssh;
        result.Vm1 = gate.Vm1;
        result.MinecraftUnit = gate.MinecraftUnit;
        result.IdleDisabled = gate.IdleDisabled;
        foreach (var n in gate.Notes)
        {
            if (!result.Notes.Contains(n, StringComparer.Ordinal))
                result.Notes.Add(n);
        }

        if (Directory.Exists(opt.PhaseDir))
        {
            JournalExcerpt.WritePhaseLogs(opt.PhaseDir, opt.PackId, journal, result);
            var resultPath = Path.Combine(opt.PhaseDir, "results", opt.PackId + ".yaml");
            result.Write(resultPath);
            Console.Error.WriteLine("Wrote " + resultPath);
        }

        Console.WriteLine(
            "verdict=" + PackVerdictCodec.ToYaml(result.Verdict)
            + " ready_for_next=" + (result.ReadyForNext ? "true" : "false"));
        return PackVerdictCodec.ExitCode(result.Verdict);
    }

    private static void FillExpected(PackTestResultDocument result, CatalogPack row)
    {
        result.Expected.Minecraft = row.Minecraft;
        result.Expected.Loader = row.Loader;
        result.Expected.LoaderVersion = row.LoaderVersion;
        result.Expected.JavaMajor = row.JavaMajor;
    }

    private static void FillDetected(PackTestResultDocument result, SetupPackPreview preview)
    {
        result.Detected.Minecraft = preview.DetectedMinecraftVersion;
        result.Detected.Loader = preview.DetectedLoader;
        result.Detected.LoaderVersion = preview.LoaderVersion;
        result.Detected.JavaMajor = preview.JavaMajor ?? 0;
    }

    private static string FirstNonEmpty(string a, string b) =>
        string.IsNullOrWhiteSpace(a) ? (b ?? "") : a;

    private static string? TryRefuseForeignLock(CliOptions opt)
    {
        var catalogDir = Path.GetDirectoryName(opt.CatalogPath);
        if (string.IsNullOrEmpty(catalogDir))
            return null;
        var lockPath = Path.Combine(catalogDir, ".lock");
        if (!File.Exists(lockPath))
            return null;

        string holder;
        try
        {
            holder = File.ReadAllText(lockPath).Trim();
        }
        catch (Exception ex)
        {
            return "Could not read pack-tests/.lock: " + ex.Message;
        }

        if (holder.Length == 0)
            return "pack-tests/.lock is empty (foreign or corrupt).";

        var phaseId = new DirectoryInfo(opt.PhaseDir).Name;
        if (holder.Contains(phaseId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(holder, opt.PhaseDir, StringComparison.OrdinalIgnoreCase))
            return null;

        return "pack-tests/.lock is held by '" + YamlText.OneLine(holder)
            + "'; this run is '" + phaseId + "'.";
    }
}
