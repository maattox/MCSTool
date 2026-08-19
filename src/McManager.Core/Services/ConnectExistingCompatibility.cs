using McManager.Core.Usage;

namespace McManager.Core.Services;

public enum ConnectExistingCompatibilityLevel
{
    Compatible,
    Warn,
    Block,
}

/// <summary>
/// v1 Connect-existing gate: do not silently attach to an incompatible stack.
/// Newer <c>infra_schema</c> / document version → block. Older schema, legacy meta,
/// or <c>stack_version</c> drift → extra confirm. Auto-detect stays button-gated.
/// </summary>
public sealed class ConnectExistingDecision
{
    public required ConnectExistingCompatibilityLevel Level { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = [];

    public bool BlocksConnect => Level == ConnectExistingCompatibilityLevel.Block;
    public bool RequiresConfirm => Level == ConnectExistingCompatibilityLevel.Warn;

    public string DialogTitle => Level switch
    {
        ConnectExistingCompatibilityLevel.Block =>
            "Cannot connect — incompatible infrastructure",
        ConnectExistingCompatibilityLevel.Warn =>
            "Infrastructure version mismatch — connect anyway?",
        _ => "Existing infrastructure detected. Connect?",
    };

    public string FormatBody(string stackSummary)
    {
        var reasons = Reasons.Count == 0
            ? ""
            : "\n\n" + string.Join("\n", Reasons.Select(r => "- " + r));
        return Level switch
        {
            ConnectExistingCompatibilityLevel.Block =>
                stackSummary
                + reasons
                + "\n\nThis Manager will not attach. Update Manager, or use the app that matches this stack. "
                + "Nothing was written to config.local.json.",
            ConnectExistingCompatibilityLevel.Warn =>
                stackSummary
                + reasons
                + "\n\nThis Manager will not modify Object Storage meta or the cloud stack. "
                + "Connecting a different infra/stack version can break manage actions. "
                + "Continue only if this is the intended stack.",
            _ => stackSummary,
        };
    }

    public string HydrateError =>
        Reasons.Count == 0
            ? "This stack is incompatible with this Manager."
            : string.Join(" ", Reasons);
}

public static class ConnectExistingCompatibility
{
    public static ConnectExistingDecision Evaluate(ConnectExistingCandidate candidate) =>
        Evaluate(candidate.Document, candidate.IsLegacy);

    public static ConnectExistingDecision Evaluate(InfraMetaConnectRead read) =>
        Evaluate(read.Document, read.IsLegacy);

    public static ConnectExistingDecision Evaluate(InfraMetaDocument? document, bool isLegacy = false)
    {
        if (document is null)
        {
            return new ConnectExistingDecision
            {
                Level = ConnectExistingCompatibilityLevel.Block,
                Reasons = ["This stack has no readable meta/infra.json."],
            };
        }

        var schema = document.InfraSchemaValue;
        var version = document.Version;
        if (schema > InfraMetaDocument.InfraSchema || version > InfraMetaDocument.DocumentVersion)
        {
            return new ConnectExistingDecision
            {
                Level = ConnectExistingCompatibilityLevel.Block,
                Reasons =
                [
                    $"This stack is newer than this Manager "
                    + $"(document version={version}, infra_schema={schema}; "
                    + $"this Manager supports version={InfraMetaDocument.DocumentVersion}, "
                    + $"infra_schema={InfraMetaDocument.InfraSchema}).",
                ],
            };
        }

        var reasons = new List<string>();
        if (isLegacy || schema < InfraMetaDocument.InfraSchema)
        {
            reasons.Add(
                $"infra_schema is {schema} (this Manager expects {InfraMetaDocument.InfraSchema}). "
                + "Connect will not migrate or modify Object Storage meta.");
        }

        if (version < InfraMetaDocument.DocumentVersion)
        {
            reasons.Add(
                $"Document version is {version} (this Manager writes {InfraMetaDocument.DocumentVersion}). "
                + "Connect will not modify the stack.");
        }

        var stack = document.StackVersion?.Trim() ?? "";
        if (!string.IsNullOrEmpty(stack)
            && !string.Equals(stack, InfraMetaDocument.DefaultStackVersion, StringComparison.Ordinal))
        {
            reasons.Add(
                $"stack_version is '{stack}' (this Manager's bundled stack is '{InfraMetaDocument.DefaultStackVersion}'). "
                + "On-box software may not match this app.");
        }

        if (!string.IsNullOrWhiteSpace(document.Mode)
            && !string.Equals(document.Mode, InfraMetaDocument.ModeAlwaysFree, StringComparison.Ordinal))
        {
            reasons.Add(
                $"mode is '{document.Mode}' (this Manager expects '{InfraMetaDocument.ModeAlwaysFree}').");
        }

        if (reasons.Count == 0)
        {
            return new ConnectExistingDecision
            {
                Level = ConnectExistingCompatibilityLevel.Compatible,
            };
        }

        return new ConnectExistingDecision
        {
            Level = ConnectExistingCompatibilityLevel.Warn,
            Reasons = reasons,
        };
    }
}
