using System.Text.Json;
using McManager.Core.Config;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FriendsLocalFileTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Leftover_mode_and_blacklist_keys_are_ignored()
    {
        var json = """
            {
              "schema_version": 1,
              "mode": "public",
              "friends": [
                { "id": "a", "name": "Ada", "ip": "203.0.113.10", "is_admin": true }
              ],
              "blacklist": [
                { "id": "b", "name": "blocked", "ip": "198.51.100.7" }
              ]
            }
            """;
        var file = JsonSerializer.Deserialize<FriendsLocalFile>(json, JsonOptions);
        Assert.NotNull(file);
        Assert.Single(file.Friends);
        Assert.Equal("Ada", file.Friends[0].Name);
        Assert.DoesNotContain("mode", JsonSerializer.Serialize(file), StringComparison.Ordinal);
        Assert.DoesNotContain("blacklist", JsonSerializer.Serialize(file), StringComparison.Ordinal);
    }

    [Fact]
    public void File_without_mode_loads_friends()
    {
        var json = """{"schema_version":1,"friends":[]}""";
        var file = JsonSerializer.Deserialize<FriendsLocalFile>(json, JsonOptions);
        Assert.NotNull(file);
        Assert.Empty(file.Friends);
    }
}
