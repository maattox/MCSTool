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
            "Cannot connect — incompatible server",
        ConnectExistingCompatibilityLevel.Warn =>
            "Different MCSTool version — connect anyway?",
        _ => "MCSTool server found. Connect?",
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
                + "\n\nMCSTool can't connect to this server. Update MCSTool, or use the version that set it up. "
                + "Nothing was changed.",
            ConnectExistingCompatibilityLevel.Warn =>
                stackSummary
                + reasons
                + "\n\nConnecting does not change anything in Oracle Cloud. "
                + "Some actions may not work if the versions differ. "
                + "Continue only if this is the right server.",
            _ => stackSummary,
        };
    }

    public string HydrateError =>
        Reasons.Count == 0
            ? "That server is incompatible with this version of MCSTool."
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
                Reasons = ["This server has no readable details in cloud storage."],
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
                    $"This server was set up by a newer version of MCSTool "
                    + $"(version {version}, format {schema}; "
                    + $"this version supports {InfraMetaDocument.DocumentVersion}, "
                    + $"format {InfraMetaDocument.InfraSchema}).",
                ],
            };
        }

        var reasons = new List<string>();
        if (isLegacy || schema < InfraMetaDocument.InfraSchema)
        {
            reasons.Add(
                $"This server was set up by an older version of MCSTool (format {schema}; this version expects {InfraMetaDocument.InfraSchema}). "
                + "Connecting does not change it.");
        }

        if (version < InfraMetaDocument.DocumentVersion)
        {
            reasons.Add(
                $"This server was set up by an older version of MCSTool (version {version}; this version writes {InfraMetaDocument.DocumentVersion}). "
                + "Connecting does not change it.");
        }

        var stack = document.StackVersion?.Trim() ?? "";
        if (!string.IsNullOrEmpty(stack)
            && !string.Equals(stack, InfraMetaDocument.DefaultStackVersion, StringComparison.Ordinal))
        {
            reasons.Add(
                $"This server was set up by a different version of MCSTool ({stack}; this version is {InfraMetaDocument.DefaultStackVersion}). "
                + "Software on the VMs may not match this app.");
        }

        if (!string.IsNullOrWhiteSpace(document.Mode)
            && !string.Equals(document.Mode, InfraMetaDocument.ModeAlwaysFree, StringComparison.Ordinal))
        {
            reasons.Add(
                $"This server uses mode '{document.Mode}' (MCSTool expects '{InfraMetaDocument.ModeAlwaysFree}').");
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
