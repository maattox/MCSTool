using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Unions local friends, Security List rows, and Object Storage entries by
/// normalized IP/CIDR. Does not pick a single source of truth.
/// </summary>
public static class FriendAllowlistMerger
{
    public static FriendAllowlistMergeResult Merge(
        IEnumerable<FriendEntry>? local,
        IEnumerable<FriendEntry>? securityList,
        IEnumerable<FriendEntry>? objectStorage,
        bool objectStoragePresent)
    {
        var localList = NormalizeList(local);
        var slList = NormalizeList(securityList);
        var osList = NormalizeList(objectStorage);

        var byIp = new Dictionary<string, MergedFriend>(StringComparer.Ordinal);
        var order = new List<string>();

        void Absorb(IReadOnlyList<NormalizedFriend> source, FriendOrigin origin)
        {
            foreach (var row in source)
            {
                if (!byIp.TryGetValue(row.Stored, out var acc))
                {
                    acc = new MergedFriend { Stored = row.Stored };
                    byIp[row.Stored] = acc;
                    order.Add(row.Stored);
                }

                if (origin == FriendOrigin.Local && acc.LocalId.Length == 0 && row.Id.Length > 0)
                    acc.LocalId = row.Id;
                if (origin == FriendOrigin.ObjectStorage && acc.OsId.Length == 0 && row.Id.Length > 0)
                    acc.OsId = row.Id;

                if (origin == FriendOrigin.Local && acc.LocalName.Length == 0 && row.Name.Length > 0)
                    acc.LocalName = row.Name;
                if (origin == FriendOrigin.ObjectStorage && acc.OsName.Length == 0 && row.Name.Length > 0)
                    acc.OsName = row.Name;
                if (origin == FriendOrigin.SecurityList && acc.SlName.Length == 0 && row.Name.Length > 0)
                    acc.SlName = row.Name;

                acc.IsAdmin |= row.IsAdmin;
            }
        }

        Absorb(localList, FriendOrigin.Local);
        Absorb(osList, FriendOrigin.ObjectStorage);
        Absorb(slList, FriendOrigin.SecurityList);

        var merged = new List<FriendEntry>(order.Count);
        foreach (var ip in order)
        {
            var acc = byIp[ip];
            var id = acc.LocalId.Length > 0
                ? acc.LocalId
                : acc.OsId.Length > 0
                    ? acc.OsId
                    : Guid.NewGuid().ToString();
            var name = acc.LocalName.Length > 0
                ? acc.LocalName
                : acc.OsName.Length > 0
                    ? acc.OsName
                    : acc.SlName;
            merged.Add(new FriendEntry
            {
                Id = id,
                Name = name,
                Ip = acc.Stored,
                IsAdmin = acc.IsAdmin,
            });
        }

        var localDiffers = !SameAllowlist(localList.Select(ToEntry), merged);
        var slDiffers = !SameAllowlist(slList.Select(ToEntry), merged);
        var osDiffers = objectStoragePresent
            ? !SameAllowlist(osList.Select(ToEntry), merged)
            : merged.Count > 0;

        return new FriendAllowlistMergeResult
        {
            Merged = merged,
            LocalDiffers = localDiffers,
            SecurityListMembershipDiffers = slDiffers,
            ObjectStorageDiffers = osDiffers,
        };
    }

    public static bool SameAllowlist(
        IEnumerable<FriendEntry>? left,
        IEnumerable<FriendEntry>? right)
    {
        var a = Keys(left);
        var b = Keys(right);
        return a.SetEquals(b);
    }

    private static HashSet<string> Keys(IEnumerable<FriendEntry>? friends)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in NormalizeList(friends))
            set.Add($"{row.Stored}\t{row.Name}\t{row.IsAdmin}");
        return set;
    }

    private static IReadOnlyList<NormalizedFriend> NormalizeList(IEnumerable<FriendEntry>? friends)
    {
        if (friends is null)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<NormalizedFriend>();
        foreach (var friend in friends)
        {
            if (!FriendRules.TryNormalizeAllowlistSource(friend.Ip ?? "", out var source, out _))
                continue;
            if (!seen.Add(source.Stored))
                continue;

            list.Add(new NormalizedFriend(
                (friend.Id ?? "").Trim(),
                DisplayName(friend.Name, source.Stored),
                source.Stored,
                friend.IsAdmin));
        }

        return list;
    }

    private static string DisplayName(string? name, string stored)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0)
            return "";
        if (string.Equals(n, stored, StringComparison.OrdinalIgnoreCase))
            return "";
        if (FriendRules.TryNormalizeAllowlistSource(n, out var parsed, out _)
            && string.Equals(parsed.Stored, stored, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return n;
    }

    private static FriendEntry ToEntry(NormalizedFriend row) =>
        new()
        {
            Id = row.Id,
            Name = row.Name,
            Ip = row.Stored,
            IsAdmin = row.IsAdmin,
        };

    private enum FriendOrigin
    {
        Local,
        SecurityList,
        ObjectStorage,
    }

    private readonly record struct NormalizedFriend(string Id, string Name, string Stored, bool IsAdmin);

    private sealed class MergedFriend
    {
        public string Stored { get; init; } = "";
        public string LocalId { get; set; } = "";
        public string OsId { get; set; } = "";
        public string LocalName { get; set; } = "";
        public string OsName { get; set; } = "";
        public string SlName { get; set; } = "";
        public bool IsAdmin { get; set; }
    }
}

public sealed class FriendAllowlistMergeResult
{
    public required IReadOnlyList<FriendEntry> Merged { get; init; }
    public bool LocalDiffers { get; init; }
    public bool SecurityListMembershipDiffers { get; init; }
    public bool ObjectStorageDiffers { get; init; }

    public bool AnyDiffers =>
        LocalDiffers || SecurityListMembershipDiffers || ObjectStorageDiffers;
}
