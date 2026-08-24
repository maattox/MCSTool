using System.Globalization;
using System.Text;

namespace McManager.PackTestHarness;

internal enum PackVerdict
{
    Pass,
    PassQuarantined,
    BlockedFreeze,
    ProductFail,
    Timeout,
    InfraFail,
}

internal static class PackVerdictCodec
{
    public static string ToYaml(PackVerdict v) => v switch
    {
        PackVerdict.Pass => "pass",
        PackVerdict.PassQuarantined => "pass_quarantined",
        PackVerdict.BlockedFreeze => "blocked_freeze",
        PackVerdict.ProductFail => "product_fail",
        PackVerdict.Timeout => "timeout",
        PackVerdict.InfraFail => "infra_fail",
        _ => "infra_fail",
    };

    public static int ExitCode(PackVerdict v) => v switch
    {
        PackVerdict.Pass or PackVerdict.PassQuarantined => 0,
        PackVerdict.ProductFail or PackVerdict.BlockedFreeze or PackVerdict.Timeout => 1,
        PackVerdict.InfraFail => 2,
        _ => 2,
    };
}

internal sealed class IdentitySlice
{
    public string Minecraft { get; set; } = "";
    public string Loader { get; set; } = "";
    public string LoaderVersion { get; set; } = "";
    public int JavaMajor { get; set; }
}

internal sealed class PackTestResultDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string PackId { get; set; } = "";
    public string Filename { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public PackVerdict Verdict { get; set; } = PackVerdict.InfraFail;
    public bool ReadyForNext { get; set; }
    public string FailMessage { get; set; } = "";
    public IdentitySlice Expected { get; init; } = new();
    public IdentitySlice Detected { get; init; } = new();
    public IdentitySlice Applied { get; init; } = new();
    public int AutomaticClient { get; set; }
    public int UnknownKept { get; set; }
    public bool RconList { get; set; }
    public bool CrashLoop { get; set; }
    public bool Fatal { get; set; }
    public bool Quarantine { get; set; }
    public string LogExcerptPath { get; set; } = "";
    public bool Ssh { get; set; }
    public string Vm1 { get; set; } = "";
    public string MinecraftUnit { get; set; } = "";
    public bool IdleDisabled { get; set; } = true;
    public List<string> Notes { get; } = [];

    public void Write(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var sb = new StringBuilder();
        sb.AppendLine("schema_version: 1");
        sb.AppendLine("pack_id: " + YamlText.Quote(PackId));
        sb.AppendLine("filename: " + YamlText.Quote(Filename));
        sb.AppendLine("sha256: " + YamlText.Quote(Sha256));
        sb.AppendLine("started_utc: " + YamlText.Quote(StartedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        sb.AppendLine("finished_utc: " + YamlText.Quote(FinishedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        sb.AppendLine("verdict: " + PackVerdictCodec.ToYaml(Verdict));
        sb.AppendLine("ready_for_next: " + (ReadyForNext ? "true" : "false"));
        sb.AppendLine("fail_message: " + YamlText.Quote(YamlText.OneLine(FailMessage)));
        sb.AppendLine("identity:");
        WriteSlice(sb, "  expected", Expected);
        WriteSlice(sb, "  detected", Detected);
        WriteSlice(sb, "  applied", Applied);
        sb.AppendLine("skip_counts:");
        sb.AppendLine("  automatic_client: " + AutomaticClient.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("  unknown_kept: " + UnknownKept.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("health:");
        sb.AppendLine("  rcon_list: " + (RconList ? "true" : "false"));
        sb.AppendLine("  crash_loop: " + (CrashLoop ? "true" : "false"));
        sb.AppendLine("  fatal: " + (Fatal ? "true" : "false"));
        sb.AppendLine("  quarantine: " + (Quarantine ? "true" : "false"));
        sb.AppendLine("log_excerpt_path: " + YamlText.Quote(LogExcerptPath));
        sb.AppendLine("infra:");
        sb.AppendLine("  ssh: " + (Ssh ? "true" : "false"));
        sb.AppendLine("  vm1: " + YamlText.Quote(Vm1));
        sb.AppendLine("  minecraft_unit: " + YamlText.Quote(MinecraftUnit));
        sb.AppendLine("  idle_disabled: " + (IdleDisabled ? "true" : "false"));
        if (Notes.Count == 0)
            sb.AppendLine("notes: []");
        else
        {
            sb.AppendLine("notes:");
            foreach (var n in Notes)
                sb.AppendLine("  - " + YamlText.Quote(n));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteSlice(StringBuilder sb, string name, IdentitySlice slice)
    {
        sb.AppendLine(name + ":");
        sb.AppendLine("    minecraft: " + YamlText.Quote(slice.Minecraft));
        sb.AppendLine("    loader: " + YamlText.Quote(slice.Loader));
        sb.AppendLine("    loader_version: " + YamlText.Quote(slice.LoaderVersion));
        sb.AppendLine("    java_major: " + slice.JavaMajor.ToString(CultureInfo.InvariantCulture));
    }
}
