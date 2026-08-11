namespace McManager.Core.Services;

public sealed class SecurityListApplyResult
{
    public int PreservedRuleCount { get; init; }
    public int OwnedRuleCount { get; init; }

    public string Summary =>
        $"Security List updated — preserved {PreservedRuleCount} rule(s), wrote {OwnedRuleCount} owned rule(s).";
}
