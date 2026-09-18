using McManager.Core.Config;
using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FriendAllowlistMergerTests
{
    private static readonly FriendEntry LocalAlice = new()
    {
        Id = "local-alice",
        Name = "Alice",
        Ip = "203.0.113.10",
        IsAdmin = false,
    };

    private static readonly FriendEntry OsBob = new()
    {
        Id = "os-bob",
        Name = "Bob",
        Ip = "198.51.100.7",
        IsAdmin = true,
    };

    [Fact]
    public void Union_keeps_every_ip_and_does_not_pick_one_source()
    {
        var slOnly = new FriendEntry { Name = "Cara", Ip = "192.0.2.10" };

        var result = FriendAllowlistMerger.Merge(
            [LocalAlice],
            [slOnly],
            [OsBob],
            objectStoragePresent: true);

        Assert.Equal(3, result.Merged.Count);
        Assert.Contains(result.Merged, f => f.Ip == "203.0.113.10" && f.Name == "Alice");
        Assert.Contains(result.Merged, f => f.Ip == "198.51.100.7" && f.Name == "Bob" && f.IsAdmin);
        Assert.Contains(result.Merged, f => f.Ip == "192.0.2.10" && f.Name == "Cara");
        Assert.True(result.AnyDiffers);
        Assert.True(result.LocalDiffers);
        Assert.True(result.SecurityListMembershipDiffers);
        Assert.True(result.ObjectStorageDiffers);
    }

    [Fact]
    public void Same_ip_prefers_local_name_and_ors_admin()
    {
        var sl = new FriendEntry { Name = "FromSl", Ip = "203.0.113.10", IsAdmin = true };
        var os = new FriendEntry { Id = "os-alice", Name = "FromOs", Ip = "203.0.113.10/32" };

        var result = FriendAllowlistMerger.Merge(
            [LocalAlice],
            [sl],
            [os],
            objectStoragePresent: true);

        var row = Assert.Single(result.Merged);
        Assert.Equal("local-alice", row.Id);
        Assert.Equal("Alice", row.Name);
        Assert.Equal("203.0.113.10", row.Ip);
        Assert.True(row.IsAdmin);
    }

    [Fact]
    public void Missing_object_storage_is_treated_as_empty_and_create_when_merged_has_rows()
    {
        var result = FriendAllowlistMerger.Merge(
            [LocalAlice],
            [LocalAlice],
            objectStorage: null,
            objectStoragePresent: false);

        Assert.False(result.LocalDiffers);
        Assert.False(result.SecurityListMembershipDiffers);
        Assert.True(result.ObjectStorageDiffers);
        Assert.Equal("Alice", Assert.Single(result.Merged).Name);
    }

    [Fact]
    public void Missing_object_storage_with_empty_merge_does_not_need_create()
    {
        var result = FriendAllowlistMerger.Merge(
            [],
            [],
            objectStorage: null,
            objectStoragePresent: false);

        Assert.False(result.AnyDiffers);
        Assert.Empty(result.Merged);
    }

    [Fact]
    public void SameAllowlist_ignores_ids_and_order_and_slash32()
    {
        var a = new FriendEntry { Id = "1", Name = "Alice", Ip = "203.0.113.10/32", IsAdmin = true };
        var b = new FriendEntry { Id = "2", Name = "alice", Ip = "203.0.113.10", IsAdmin = true };
        Assert.True(FriendAllowlistMerger.SameAllowlist([a], [b]));
    }

    [Fact]
    public void Name_equal_to_ip_is_treated_as_unnamed()
    {
        var local = new FriendEntry { Name = "", Ip = "203.0.113.10" };
        var sl = new FriendEntry { Name = "203.0.113.10", Ip = "203.0.113.10" };

        var result = FriendAllowlistMerger.Merge(
            [local],
            [sl],
            objectStorage: null,
            objectStoragePresent: false);

        Assert.False(result.LocalDiffers);
        Assert.Equal("", Assert.Single(result.Merged).Name);
    }

    [Fact]
    public void Local_order_then_os_then_security_list()
    {
        var result = FriendAllowlistMerger.Merge(
            [LocalAlice],
            [new FriendEntry { Name = "Cara", Ip = "192.0.2.10" }],
            [OsBob],
            objectStoragePresent: true);

        Assert.Equal(["203.0.113.10", "198.51.100.7", "192.0.2.10"], result.Merged.Select(f => f.Ip));
    }

    [Fact]
    public void Duplicate_ips_in_one_source_collapse()
    {
        var dup = new FriendEntry { Name = "Other", Ip = "203.0.113.10" };
        var result = FriendAllowlistMerger.Merge(
            [LocalAlice, dup],
            [],
            [],
            objectStoragePresent: true);

        var row = Assert.Single(result.Merged);
        Assert.Equal("Alice", row.Name);
    }
}
