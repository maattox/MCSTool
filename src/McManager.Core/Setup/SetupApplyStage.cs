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

    /// <summary>0–100 from known apply stages. <see cref="NotStarted"/> is 0; <see cref="ConfigWritten"/> is 100.</summary>
    public static int Percent(string? stage)
    {
        var max = Order.Length - 1;
        if (max <= 0)
            return 0;
        return (int)Math.Round(100.0 * IndexOf(stage) / max);
    }

    public static string DisplayName(string? stage) => (stage ?? "").Trim() switch
    {
        TofuApplied => "Cloud resources",
        CloudInit => "Waiting for VMs",
        Door => "Door software",
        Vm1 => "Minecraft install",
        OsMeta => "Shared storage",
        Function => "Spend-brake Function",
        ConfigWritten => "Saving local config",
        _ => "Waiting to start",
    };

    public static SetupProgressUpdate Update(string stage, string? caption = null) =>
        new(stage, Percent(stage), caption ?? DisplayName(stage));
}

/// <summary>UI-only deploy progress. Does not persist; dry-run may report 100% without changing apply_stage.</summary>
public readonly record struct SetupProgressUpdate(string Stage, int Percent, string Caption);
