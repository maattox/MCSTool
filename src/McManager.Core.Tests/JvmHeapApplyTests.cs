using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class JvmHeapApplyTests
{
    [Fact]
    public void Finds_onbox_script()
    {
        var path = JvmHeapApply.FindLocalScript();
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path!);
        Assert.Contains("-Xms", text, StringComparison.Ordinal);
        Assert.Contains("-Xmx", text, StringComparison.Ordinal);
        Assert.Contains("paper-jvm-flags.json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-Dusing.aikars.flags", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_command_is_elevated_python_plus_daemon_reload()
    {
        var cmd = JvmHeapApply.RunCommand("6G");
        Assert.Contains("sudo bash -c", cmd, StringComparison.Ordinal);
        Assert.Contains("python3", cmd, StringComparison.Ordinal);
        Assert.Contains("/tmp/mcmgr-heap/apply-jvm-heap.py", cmd, StringComparison.Ordinal);
        Assert.Contains("'6G'", cmd, StringComparison.Ordinal);
        Assert.Contains("systemctl daemon-reload", cmd, StringComparison.Ordinal);
        Assert.Contains("HOME=\"${HOME:-/home/ubuntu}\"", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_ok_line()
    {
        Assert.True(JvmHeapApply.TryParseOk("OK heap=6G paper_unit=1\n", out var heap, out var error), error);
        Assert.Equal("6G", heap);
    }

    [Fact]
    public void Rejects_missing_ok()
    {
        Assert.False(JvmHeapApply.TryParseOk("nope", out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
