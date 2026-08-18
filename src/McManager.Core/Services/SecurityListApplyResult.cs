namespace McManager.Core.Services;

public sealed class SecurityListApplyResult
{
    public int PreservedRuleCount { get; init; }
    public int OwnedRuleCount { get; init; }
    public bool PublicMinecraft { get; init; }

    public string Summary =>
        PublicMinecraft
            ? $"Security List updated — Minecraft 25565 TCP/UDP from 0.0.0.0/0; preserved {PreservedRuleCount} rule(s), wrote {OwnedRuleCount} owned rule(s). SSH is not world-open."
            : $"Security List updated — preserved {PreservedRuleCount} rule(s), wrote {OwnedRuleCount} owned rule(s).";
}
