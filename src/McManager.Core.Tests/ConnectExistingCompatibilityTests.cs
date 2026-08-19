using System.Text;
using System.Text.Json.Nodes;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ConnectExistingCompatibilityTests
{
    [Fact]
    public void Compatible_fixture_is_not_blocked()
    {
        var parsed = InfraMetaStore.ParseForConnect(ReadBytes());
        Assert.True(parsed.Succeeded);
        Assert.NotNull(parsed.Value);
        Assert.False(parsed.Value.Skipped);
        Assert.NotNull(parsed.Value.Document);

        var errors = parsed.Value.Document.ValidateForConnect(out var warnings);
        Assert.Empty(errors);
        Assert.Empty(warnings);

        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value);
        Assert.Equal(ConnectExistingCompatibilityLevel.Compatible, decision.Level);
        Assert.False(decision.BlocksConnect);
        Assert.False(decision.RequiresConfirm);
    }

    [Fact]
    public void Newer_infra_schema_blocks_connect()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(schema: 99));
        Assert.True(parsed.Succeeded);
        Assert.NotNull(parsed.Value?.Document);

        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value);
        Assert.Equal(ConnectExistingCompatibilityLevel.Block, decision.Level);
        Assert.True(decision.BlocksConnect);
        Assert.Contains("infra_schema=99", decision.Reasons[0], StringComparison.Ordinal);
        Assert.Contains("Cannot connect", decision.DialogTitle, StringComparison.Ordinal);
        Assert.Contains("will not attach", decision.FormatBody("summary"), StringComparison.Ordinal);
    }

    [Fact]
    public void Newer_document_version_blocks_connect()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(version: 99));
        Assert.True(parsed.Succeeded);
        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value!);
        Assert.True(decision.BlocksConnect);
        Assert.Contains("document version=99", decision.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Older_infra_schema_warns_and_does_not_block()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(schema: 1));
        Assert.True(parsed.Succeeded);
        Assert.NotNull(parsed.Value?.Document);

        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value);
        Assert.Equal(ConnectExistingCompatibilityLevel.Warn, decision.Level);
        Assert.False(decision.BlocksConnect);
        Assert.True(decision.RequiresConfirm);
        Assert.Contains("infra_schema is 1", decision.Reasons[0], StringComparison.Ordinal);
        Assert.Contains("connect anyway", decision.DialogTitle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Stack_version_mismatch_warns()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(stackVersion: "9.9.9"));
        Assert.True(parsed.Succeeded);

        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value!);
        Assert.Equal(ConnectExistingCompatibilityLevel.Warn, decision.Level);
        Assert.Contains("stack_version is '9.9.9'", string.Join(" ", decision.Reasons), StringComparison.Ordinal);
        Assert.Contains(InfraMetaDocument.DefaultStackVersion, string.Join(" ", decision.Reasons), StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_flag_warns_even_when_schema_matches()
    {
        var parsed = InfraMetaStore.ParseForConnect(ReadBytes());
        Assert.NotNull(parsed.Value?.Document);
        var decision = ConnectExistingCompatibility.Evaluate(parsed.Value.Document, isLegacy: true);
        Assert.Equal(ConnectExistingCompatibilityLevel.Warn, decision.Level);
        Assert.Contains("infra_schema is 2", decision.Reasons[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hydrate_refuses_newer_schema_without_writing()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(schema: 3));
        Assert.NotNull(parsed.Value?.Document);
        var candidate = Candidate(parsed.Value.Document, parsed.Value.IsLegacy);

        var result = await ConnectExistingService.HydrateAsync(candidate, @"C:\keys\mcmgr_ed25519");
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains("newer than this Manager", result.Error, StringComparison.Ordinal);
        Assert.Contains("infra_schema=3", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hydrate_allows_stack_version_mismatch_after_confirm_gate()
    {
        var parsed = InfraMetaStore.ParseForConnect(MutateBytes(stackVersion: "0.2.0"));
        Assert.NotNull(parsed.Value?.Document);
        var candidate = Candidate(parsed.Value.Document, parsed.Value.IsLegacy);

        var decision = ConnectExistingCompatibility.Evaluate(candidate);
        Assert.True(decision.RequiresConfirm);

        var result = await ConnectExistingService.HydrateAsync(candidate, @"C:\keys\mcmgr_ed25519");
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.Equal("ocid1.instance.oc1..aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", result.Value.Vm1.InstanceId);
        Assert.Equal(@"C:\keys\mcmgr_ed25519", result.Value.Vm1.SshKeyPath);
    }

    [Fact]
    public void Connect_summary_includes_schema_and_stack_version()
    {
        var parsed = InfraMetaStore.ParseForConnect(ReadBytes());
        var summary = parsed.Value!.Document!.FormatConnectSummary("TESTING", "mcmgr");
        Assert.Contains("Infra schema: 2", summary, StringComparison.Ordinal);
        Assert.Contains("stack 0.1.0", summary, StringComparison.Ordinal);
        Assert.Contains("203.0.113.10", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Chooser_marks_incompatible_and_version_warning()
    {
        var blocked = Candidate(
            InfraMetaStore.ParseForConnect(MutateBytes(schema: 99)).Value!.Document!,
            isLegacy: false);
        Assert.Contains("(incompatible)", blocked.ChooserLabel, StringComparison.Ordinal);

        var warned = Candidate(
            InfraMetaStore.ParseForConnect(MutateBytes(stackVersion: "0.2.0")).Value!.Document!,
            isLegacy: false);
        Assert.Contains("(version warning)", warned.ChooserLabel, StringComparison.Ordinal);

        var ok = Candidate(
            InfraMetaStore.ParseForConnect(ReadBytes()).Value!.Document!,
            isLegacy: false);
        Assert.DoesNotContain("incompatible", ok.ChooserLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("version warning", ok.ChooserLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_document_blocks()
    {
        var decision = ConnectExistingCompatibility.Evaluate(document: null);
        Assert.True(decision.BlocksConnect);
        Assert.Contains("no readable meta/infra.json", decision.Reasons[0], StringComparison.Ordinal);
    }

    private static ConnectExistingCandidate Candidate(InfraMetaDocument doc, bool isLegacy) =>
        new()
        {
            ProfileName = "TESTING",
            OciConfigFile = "%USERPROFILE%\\.oci\\config",
            Region = doc.Region,
            TenancyId = doc.TenancyId,
            CompartmentId = doc.CompartmentId,
            CompartmentName = "mcmgr",
            Namespace = doc.ObjectStorage.Namespace,
            Bucket = doc.ObjectStorage.Bucket,
            Document = doc,
            IsLegacy = isLegacy,
        };

    private static byte[] ReadBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "infra", "connect-compatible.json");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");
        return File.ReadAllBytes(path);
    }

    private static byte[] MutateBytes(int? version = null, int? schema = null, string? stackVersion = null)
    {
        var root = JsonNode.Parse(Encoding.UTF8.GetString(ReadBytes()))!.AsObject();
        if (version.HasValue)
            root["version"] = version.Value;
        if (schema.HasValue)
            root["infra_schema"] = schema.Value;
        if (stackVersion is not null)
            root["stack_version"] = stackVersion;
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}
