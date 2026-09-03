using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FunctionImageConvergeTests
{
    [Fact]
    public void Username_derives_namespace_domain_iam_name()
    {
        var result = OcirUsername.Derive("idbxxxexample", "alice@example.com");
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("idbxxxexample/Default/alice@example.com", result.Value);
    }

    [Fact]
    public void Username_classic_two_part_when_domain_blank()
    {
        var result = OcirUsername.Derive("idbxxxexample", "alice", identityDomain: "");
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("idbxxxexample/alice", result.Value);
    }

    [Fact]
    public void Username_uses_listed_identity_domain()
    {
        var result = OcirUsername.Derive("ns", "alice", identityDomain: "Corp");
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("ns/Corp/alice", result.Value);
    }

    [Fact]
    public void Username_rejects_user_ocid()
    {
        var result = OcirUsername.Derive("idbxxxexample", "ocid1.user.oc1..example");
        Assert.False(result.Succeeded);
        Assert.Contains("not the ~/.oci user= OCID", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "user")]
    [InlineData("", "user")]
    [InlineData("   ", "user")]
    [InlineData("ns", null)]
    [InlineData("ns", "")]
    [InlineData("ns", "  ")]
    [InlineData(null, null)]
    public void Username_rejects_blank_namespace_or_user(string? ns, string? user)
    {
        var result = OcirUsername.Derive(ns, user);
        Assert.False(result.Succeeded);
        Assert.Contains("required", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Username_env_override_wins()
    {
        var result = OcirUsername.Resolve("ns", "oci-user", envOverride: "ns/override-user");
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("ns/override-user", result.Value);
    }

    [Fact]
    public void Digest_needs_copy_when_live_missing_or_different()
    {
        Assert.True(FunctionImageDigest.NeedsCopy("sha256:abc", liveDigest: null));
        Assert.True(FunctionImageDigest.NeedsCopy("sha256:abc", ""));
        Assert.True(FunctionImageDigest.NeedsCopy("sha256:abc", "sha256:def"));
        Assert.False(FunctionImageDigest.NeedsCopy("sha256:ABC", "ABC"));
        Assert.False(FunctionImageDigest.NeedsCopy("sha256:abc", "sha256:abc"));
        Assert.False(FunctionImageDigest.NeedsCopy(null, "sha256:abc"));
        Assert.False(FunctionImageDigest.NeedsCopy("", "sha256:abc"));
    }

    [Fact]
    public void Should_attempt_when_stage_is_already_function_if_tar_exists()
    {
        Assert.True(FunctionImageDeployer.ShouldAttempt(
            SetupApplyStage.Function,
            @"C:\app\mcmgr-fn-softstop-linux-arm64.tar"));
        Assert.True(FunctionImageDeployer.ShouldAttempt(
            SetupApplyStage.ConfigWritten,
            @"C:\app\mcmgr-fn-softstop-linux-arm64.tar"));
        Assert.False(FunctionImageDeployer.ShouldAttempt(SetupApplyStage.Function, artifactPath: null));
        Assert.False(FunctionImageDeployer.ShouldAttempt(SetupApplyStage.ConfigWritten, ""));
        Assert.True(FunctionImageDeployer.ShouldAttempt(SetupApplyStage.OsMeta, artifactPath: null));
    }

    [Fact]
    public async Task Stage_already_function_but_digest_differs_still_copies()
    {
        var previous = Environment.GetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar);
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-fn-cvg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Environment.SetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar, "1");
            var workspace = new TofuWorkspace(dir);
            var tofu = new RecordingOpenTofuRunner();
            var publisher = new FakePublisher(
                copied: true,
                image: "sjc.ocir.io/dryrunns/mcmgr-fn/softstop:setup");
            var state = NewWizardState();
            state.ApplyStage = SetupApplyStage.Function;
            var outputs = TofuApplyOutputs.Parse(TofuApplyOutputs.CannedDryRunJson).Value!;

            Assert.True(FunctionImageDeployer.ShouldAttempt(
                state.ApplyStage,
                Path.Combine(dir, FunctionImageArtifact.FileName)));

            var result = await FunctionImageDeployer.RunAsync(
                publisher,
                tofu,
                infraDirectory: dir,
                workspace,
                state,
                outputs,
                log: null);

            Assert.Null(result.SkipReason);
            Assert.True(result.Copied);
            Assert.True(result.Applied);
            Assert.Equal(1, publisher.Calls);
            Assert.Contains("apply", tofu.Commands);
            Assert.Equal("sjc.ocir.io/dryrunns/mcmgr-fn/softstop:setup", state.FunctionImage);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar, previous);
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Matching_digest_does_not_apply()
    {
        var previous = Environment.GetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar);
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-fn-same-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Environment.SetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar, "1");
            var tofu = new RecordingOpenTofuRunner();
            var publisher = new FakePublisher(
                copied: false,
                image: "sjc.ocir.io/dryrunns/mcmgr-fn/softstop:setup");
            var state = NewWizardState();
            state.ApplyStage = SetupApplyStage.ConfigWritten;
            var outputs = TofuApplyOutputs.Parse(TofuApplyOutputs.CannedDryRunJson).Value!;

            var result = await FunctionImageDeployer.RunAsync(
                publisher,
                tofu,
                infraDirectory: dir,
                new TofuWorkspace(dir),
                state,
                outputs,
                log: null);

            Assert.Null(result.SkipReason);
            Assert.False(result.Copied);
            Assert.False(result.Applied);
            Assert.Empty(tofu.Commands);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProductPaths.TofuDryRunEnvVar, previous);
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Skipped_push_does_not_rewrite_or_apply()
    {
        var tofu = new RecordingOpenTofuRunner();
        var publisher = new FakePublisher(error: "No Auth Token in Windows Credential Manager (MCSTool/ocir). Function/Events stay skipped.");
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-fn-skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var result = await FunctionImageDeployer.RunAsync(
                publisher,
                tofu,
                infraDirectory: dir,
                new TofuWorkspace(dir),
                NewWizardState(),
                TofuApplyOutputs.Parse(TofuApplyOutputs.CannedDryRunJson).Value!,
                log: null);

            Assert.Contains("Auth Token", result.SkipReason, StringComparison.Ordinal);
            Assert.False(result.Copied);
            Assert.Empty(tofu.Commands);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static SetupWizardState NewWizardState() => new()
    {
        EulaAccepted = true,
        OciProfile = "TESTING",
        OciRegion = "us-sanjose-1",
        AdminCidr = "203.0.113.10/32",
        SshPublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAItest testhost",
        AlertEmail = "ops@example.com",
        CompartmentName = "mcmgr",
    };

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class FakePublisher : IFunctionImagePublisher
    {
        private readonly bool _copied;
        private readonly string _image;
        private readonly string? _error;

        public FakePublisher(bool copied = false, string image = "", string? error = null)
        {
            _copied = copied;
            _image = image;
            _error = error;
        }

        public int Calls { get; private set; }

        public Task<ServiceResult<FunctionImagePublishResult>> TryPublishAsync(
            TofuApplyOutputs outputs,
            SetupWizardState state,
            IProgress<string>? log,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (!string.IsNullOrWhiteSpace(_error))
                return Task.FromResult(ServiceResult<FunctionImagePublishResult>.Fail(_error));
            return Task.FromResult(ServiceResult<FunctionImagePublishResult>.Ok(
                new FunctionImagePublishResult { Image = _image, Copied = _copied }));
        }
    }
}
