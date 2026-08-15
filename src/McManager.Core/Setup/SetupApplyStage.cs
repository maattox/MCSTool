namespace McManager.Core.Setup;

/// <summary>Resumable Setup apply checkpoints (wizard JSON + tofu state + on-box bootstrap-state are separate).</summary>
public static class SetupApplyStage
{
    public const string NotStarted = "not_started";
    public const string TofuApplied = "tofu_applied";
    public const string CloudInit = "cloud_init";
    public const string Door = "door";
    public const string Vm1 = "vm1";
    public const string OsMeta = "os_meta";
    public const string Function = "function";
    public const string ConfigWritten = "config_written";

    public static readonly string[] Order =
    [
        NotStarted,
        TofuApplied,
        CloudInit,
        Door,
        Vm1,
        OsMeta,
        Function,
        ConfigWritten,
    ];

    public static int IndexOf(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return 0;
        var i = Array.IndexOf(Order, stage.Trim());
        return i < 0 ? 0 : i;
    }

    public static bool Reached(string? current, string required) =>
        IndexOf(current) >= IndexOf(required);
}
