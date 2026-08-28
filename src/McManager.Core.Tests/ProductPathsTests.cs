using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ProductPathsTests
{
    [Fact]
    public void Published_layout_is_a_product_root_without_a_git_checkout()
    {
        var root = NewTempDir("product-root");
        try
        {
            WritePublishedLayout(root);

            Assert.Equal(root, ProductPaths.FindProductRepoRootFrom(root));
            Assert.False(Directory.Exists(Path.Combine(root, ".git")));

            Assert.Equal(Path.Combine(root, "infra"), ProductPaths.InfraDirectoryAt(root));
            Assert.Equal(Path.Combine(root, "onbox", "mcmgr"), ProductPaths.OnboxDirectoryAt(root));
            Assert.Equal(Path.Combine(root, "door_vm"), ProductPaths.DoorVmDirectoryAt(root));
            Assert.Equal(Path.Combine(root, "vm_agent"), ProductPaths.VmAgentDirectoryAt(root));
            Assert.Equal(Path.Combine(root, "functions", "shutdown_vm"), ProductPaths.FunctionDirectoryAt(root));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Nested_start_walks_up_to_the_published_folder()
    {
        var root = NewTempDir("product-nested");
        try
        {
            WritePublishedLayout(root);
            var nested = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(nested);

            Assert.Equal(root, ProductPaths.FindProductRepoRootFrom(nested));
            Assert.Equal(Path.Combine(root, "infra"), ProductPaths.InfraDirectoryAt(root));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Infra_without_main_tf_is_not_a_usable_infra_dir()
    {
        var root = NewTempDir("product-empty-infra");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "infra"));
            Assert.Equal(root, ProductPaths.FindProductRepoRootFrom(root));
            Assert.Null(ProductPaths.InfraDirectoryAt(root));
        }
        finally
        {
            TryDeleteDir(root);
        }
    }

    [Fact]
    public void Missing_root_resolves_to_null()
    {
        Assert.Null(ProductPaths.InfraDirectoryAt(null));
        Assert.Null(ProductPaths.OnboxDirectoryAt(null));
        Assert.Null(ProductPaths.DoorVmDirectoryAt(null));
        Assert.Null(ProductPaths.VmAgentDirectoryAt(null));
        Assert.Null(ProductPaths.FunctionDirectoryAt(null));
        Assert.Null(ProductPaths.FindProductRepoRootFrom(""));
    }

    private static void WritePublishedLayout(string root)
    {
        Write(root, Path.Combine("infra", "main.tf"), "# tofu");
        Write(root, Path.Combine("onbox", "mcmgr", "common", "driver.sh"), "#!/bin/sh");
        Write(root, Path.Combine("door_vm", "Makefile"), "all:");
        Write(root, Path.Combine("vm_agent", "install.sh"), "#!/bin/sh");
        Write(root, Path.Combine("functions", "shutdown_vm", "func.py"), "def handler():\n    pass\n");
    }

    private static void Write(string root, string relative, string contents)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-" + prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
