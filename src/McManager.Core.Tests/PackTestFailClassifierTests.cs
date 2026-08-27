using McManager.PackTestHarness;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackTestFailClassifierTests
{
    [Theory]
    [InlineData("Pack replace failed: An established connection was aborted by the server.")]
    [InlineData(
        "VM1 SSH connect failed: A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.")]
    [InlineData("VM1 is not RUNNING.")]
    [InlineData("Instance lifecycle STOPPING")]
    [InlineData("An existing connection was forcibly closed by the remote host.")]
    [InlineData("Connection reset by peer")]
    [InlineData("SocketException: WSAECONNABORTED")]
    public void FromReplaceError_classifies_ssh_and_lifecycle_as_infra(string error)
    {
        Assert.Equal(PackVerdict.InfraFail, FailClassifier.FromReplaceError(error));
        Assert.True(FailClassifier.LooksInfra(error));
    }

    [Fact]
    public void FromReplaceError_keeps_rcon_wait_as_timeout()
    {
        var err = "RCON list did not succeed in time (server may still be starting).";
        Assert.Equal(PackVerdict.Timeout, FailClassifier.FromReplaceError(err));
    }

    [Fact]
    public void FromReplaceError_keeps_pack_crash_as_product_fail()
    {
        var err = "minecraft.service keeps restarting after pack replace.";
        Assert.Equal(PackVerdict.ProductFail, FailClassifier.FromReplaceError(err));
        Assert.False(FailClassifier.LooksInfra(err));
    }
}
