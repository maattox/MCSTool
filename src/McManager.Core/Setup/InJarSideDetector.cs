using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>
/// Cheap in-jar client/server side signals for unstructured / manual / jar-root zips
/// (Step 8.7 P2) and leftover <c>.mrpack</c> jars after <c>env.server</c> + overlay (P3).
/// Pack-declared <c>env.server</c> is R2; Layer 1–2 lists stay in
/// <see cref="ExcludeIncludeMatcher"/>. Does not call CurseForge or Modrinth APIs.
/// </summary>
/// <remarks>
/// Mixin heuristic (high-confidence only): a Mixin config's <strong>common</strong>
/// <c>mixins</c> array (applied on a dedicated server) must target one of
/// <see cref="ClientClassPrefixes"/>. Mixins listed only under the config's
/// <c>client</c> array are dist-gated and are not a reason to strip. Presence of a
/// mixin JSON file alone is not a reason to strip.
/// </remarks>
internal static class InJarSideDetector
{
    /// <summary>
    /// Dedicated-server-killer class prefixes (slash form). Mixin targets and
    /// refmap strings are normalized to lowercase slash paths before matching.
    /// </summary>
    internal static readonly string[] ClientClassPrefixes =
    [
        "net/minecraft/client/",
        "com/mojang/blaze3d/",
        "net/minecraftforge/client/",
        "net/neoforged/neoforge/client/",
        "net/fabricmc/fabric/api/client/",
        "net/fabricmc/fabric/impl/client/",
        "net/fabricmc/api/client/",
    ];

    private static readonly HashSet<string> FabricClientEntrypoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "client", "client_init",
    };

    private static readonly HashSet<string> FabricDedicatedOrBothEntrypoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "main", "server", "preLaunch", "pre_launch", "init",
    };

    private static readonly Regex MinecraftVersionRegex = new(
        @"\d+\.\d+(?:\.\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal readonly record struct PeekResult(
        bool HadMetadata,
        string Environment,
        string? Loader = null,
        string? MinecraftVersion = null)
    {
        public static PeekResult None => new(false, "*");
    }

    internal static PeekResult PeekFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return PeekResult.None;

        try
        {
            var length = new FileInfo(path).Length;
            if (length <= 0 || length > ManualServerPackAnalyzer.MaxJarPeekBytes)
                return PeekResult.None;
            using var stream = File.OpenRead(path);
            return Peek(stream);
        }
        catch (IOException)
        {
            return PeekResult.None;
        }
        catch (UnauthorizedAccessException)
        {
            return PeekResult.None;
        }
    }

    public static PeekResult Peek(Stream jarStream)
    {
        ZipArchive jar;
        try
        {
            jar = new ZipArchive(jarStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            return PeekResult.None;
        }
        catch (NotSupportedException)
        {
            return PeekResult.None;
        }

        using (jar)
        {
            var fabric = FindEntry(jar, "fabric.mod.json");
            if (fabric is not null)
            {
                TryReadFabricMetadata(fabric, out var fabricEnv, out var fabricMc);
                return new PeekResult(true, fabricEnv, MrpackAnalyzer.LoaderFabric, fabricMc);
            }

            var quilt = FindEntry(jar, "quilt.mod.json");
            if (quilt is not null)
            {
                var hadQuilt = TryReadQuiltMetadata(quilt, out var quiltEnv, out var quiltMc);
                return new PeekResult(hadQuilt, quiltEnv, MrpackAnalyzer.LoaderQuilt, quiltMc);
            }

            var neoToml = FindEntry(jar, "META-INF/neoforge.mods.toml");
            var forgeToml = FindEntry(jar, "META-INF/mods.toml");
            var toml = neoToml ?? forgeToml;
            string? loader = null;
            string? tomlMc = null;
            var tomlEnv = "*";
            var hadSide = false;
            if (toml is not null)
            {
                loader = neoToml is not null ? MrpackAnalyzer.LoaderNeoForge : MrpackAnalyzer.LoaderForge;
                TryReadTomlMetadata(toml, out tomlEnv, out tomlMc, out hadSide);
                if (hadSide)
                    return new PeekResult(true, tomlEnv, loader, tomlMc);
            }

            if (HasDedicatedServerKillingMixin(jar))
                return new PeekResult(true, "client", loader, tomlMc);

            if (loader is not null || tomlMc is not null)
                return new PeekResult(false, "*", loader, tomlMc);
        }

        return PeekResult.None;
    }

    internal static bool LooksLikeClientClass(string? raw)
    {
        var normalized = NormalizeClassName(raw);
        if (normalized.Length == 0)
            return false;
        foreach (var prefix in ClientClassPrefixes)
        {
            if (normalized.Contains(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static string NormalizeClassName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim().ToLowerInvariant().Replace('\\', '/').Replace('.', '/');
        if (s.EndsWith(".class", StringComparison.Ordinal))
            s = s[..^6];
        return s;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive jar, string relative)
    {
        return jar.Entries.FirstOrDefault(e =>
            string.Equals(
                MrpackAnalyzer.NormalizeZipPath(e.FullName),
                relative,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryReadFabricMetadata(ZipArchiveEntry entry, out string environment, out string? minecraft)
    {
        environment = "*";
        minecraft = null;
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var allClient = true;
                var any = false;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    any = true;
                    if (!IsFabricClientDeclaration(item, out var itemMc))
                        allClient = false;
                    minecraft ??= itemMc;
                }

                if (!any)
                    return false;
                environment = allClient ? "client" : "*";
                return true;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            environment = IsFabricClientDeclaration(doc.RootElement, out minecraft) ? "client" : "*";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsFabricClientDeclaration(JsonElement root, out string? minecraft)
    {
        minecraft = ReadMinecraftFromDepends(root);
        var env = ReadStringProperty(root, "environment");
        if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(env))
            return false;
        return HasOnlyClientEntrypoints(root);
    }

    private static bool TryReadQuiltMetadata(ZipArchiveEntry entry, out string environment, out string? minecraft)
    {
        environment = "*";
        minecraft = null;
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement loaderRoot = default;
            var hasLoader = false;
            if (TryGetPropertyIgnoreCase(doc.RootElement, "quilt_loader", out var loader)
                && loader.ValueKind == JsonValueKind.Object)
            {
                loaderRoot = loader;
                hasLoader = true;
            }

            var sideRoot = hasLoader ? loaderRoot : doc.RootElement;
            var env = ReadStringProperty(sideRoot, "environment");
            minecraft = ReadMinecraftFromDepends(sideRoot) ?? ReadMinecraftFromDepends(doc.RootElement);
            if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
            {
                environment = "client";
                return true;
            }

            if (string.IsNullOrEmpty(env) && HasOnlyClientEntrypoints(sideRoot))
            {
                environment = "client";
                return true;
            }

            if (string.IsNullOrEmpty(env) && HasOnlyClientEntrypoints(doc.RootElement))
            {
                environment = "client";
                return true;
            }

            environment = string.IsNullOrEmpty(env) ? "*" : env.Trim().ToLowerInvariant();
            return hasLoader || !string.IsNullOrEmpty(env);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasOnlyClientEntrypoints(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "entrypoints", out var entrypoints)
            || entrypoints.ValueKind != JsonValueKind.Object)
            return false;

        var sawClient = false;
        var sawDedicatedOrBoth = false;
        foreach (var prop in entrypoints.EnumerateObject())
        {
            if (EntrypointValueEmpty(prop.Value))
                continue;
            if (FabricClientEntrypoints.Contains(prop.Name))
                sawClient = true;
            else if (FabricDedicatedOrBothEntrypoints.Contains(prop.Name))
                sawDedicatedOrBoth = true;
        }

        return sawClient && !sawDedicatedOrBoth;
    }

    private static bool EntrypointValueEmpty(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined || value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind == JsonValueKind.Array)
            return !value.EnumerateArray().Any();
        if (value.ValueKind == JsonValueKind.String)
            return string.IsNullOrWhiteSpace(value.GetString());
        return false;
    }

    private static bool TryReadTomlMetadata(
        ZipArchiveEntry entry,
        out string environment,
        out string? minecraft,
        out bool hadSide)
    {
        environment = "*";
        minecraft = null;
        hadSide = false;
        var env = "*";
        string? mc = null;
        var sideFound = false;
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var toml = reader.ReadToEnd();
            string? table = null;
            string? pendingModId = null;
            string? pendingVersion = null;
            string? pendingSide = null;
            string? pendingDisplayTest = null;
            string? pendingClientSideOnly = null;

            void FlushTable()
            {
                if (table is null)
                    return;
                var isModsTable = table.Equals("[[mods]]", StringComparison.OrdinalIgnoreCase)
                    || table.Equals("[mods]", StringComparison.OrdinalIgnoreCase);
                if (isModsTable && !sideFound)
                {
                    if (pendingClientSideOnly is not null
                        && pendingClientSideOnly.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        env = "client";
                        sideFound = true;
                    }
                    else if (pendingClientSideOnly is not null
                             && pendingClientSideOnly.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        env = "*";
                        sideFound = true;
                    }
                    else if (pendingDisplayTest is not null
                             && pendingDisplayTest.Equals("IGNORE_SERVER_VERSION", StringComparison.OrdinalIgnoreCase))
                    {
                        env = "client";
                        sideFound = true;
                    }
                    else if (pendingSide is not null)
                    {
                        env = pendingSide.Equals("CLIENT", StringComparison.OrdinalIgnoreCase) ? "client" : "*";
                        sideFound = true;
                    }
                }

                if (mc is null
                    && table.Contains("dependencies", StringComparison.OrdinalIgnoreCase)
                    && pendingModId is not null
                    && pendingModId.Equals("minecraft", StringComparison.OrdinalIgnoreCase)
                    && pendingVersion is not null
                    && TryExtractMinecraftVersion(pendingVersion, out var extracted))
                {
                    mc = extracted;
                }
            }

            foreach (var raw in toml.Split('\n'))
            {
                var line = StripTomlComment(raw.Trim());
                if (line.Length == 0)
                    continue;
                if (line.StartsWith('['))
                {
                    FlushTable();
                    table = line;
                    pendingModId = null;
                    pendingVersion = null;
                    pendingSide = null;
                    pendingDisplayTest = null;
                    pendingClientSideOnly = null;
                    continue;
                }

                if (table is null)
                    continue;
                if (TryParseTomlAssignment(line, "side", out var side))
                    pendingSide = side;
                if (TryParseTomlAssignment(line, "displayTest", out var displayTest))
                    pendingDisplayTest = displayTest;
                if (TryParseTomlAssignment(line, "clientSideOnly", out var clientSideOnly))
                    pendingClientSideOnly = clientSideOnly;
                if (TryParseTomlAssignment(line, "modId", out var modId))
                    pendingModId = modId;
                if (TryParseTomlAssignment(line, "versionRange", out var range)
                    || TryParseTomlAssignment(line, "version", out range))
                    pendingVersion = range;
            }

            FlushTable();
            environment = env;
            minecraft = mc;
            hadSide = sideFound;
            return sideFound || mc is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string StripTomlComment(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
                inQuotes = !inQuotes;
            else if (c == '#' && !inQuotes)
                return line[..i].TrimEnd();
        }

        return line;
    }

    private static bool TryParseTomlAssignment(string line, string key, out string value)
    {
        value = "";
        var eq = line.IndexOf('=');
        if (eq <= 0)
            return false;
        if (!line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            return false;
        var rhs = line[(eq + 1)..].Trim().Trim('"').Trim('\'');
        if (rhs.Length == 0)
            return false;
        value = rhs;
        return true;
    }

    private static bool HasDedicatedServerKillingMixin(ZipArchive jar)
    {
        var refmapTargets = LoadRefmapTargets(jar);
        foreach (var entry in jar.Entries)
        {
            var path = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (!IsMixinConfigPath(path))
                continue;
            if (MixinConfigHasClientTargetInCommonList(entry, refmapTargets))
                return true;
        }

        return false;
    }

    internal static bool IsMixinConfigPath(string normalizedPath)
    {
        if (string.IsNullOrEmpty(normalizedPath)
            || !normalizedPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        if (normalizedPath.EndsWith(".refmap.json", StringComparison.OrdinalIgnoreCase))
            return false;
        var leaf = normalizedPath.Split('/')[^1];
        return leaf.Contains("mixins", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MixinConfigHasClientTargetInCommonList(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, IReadOnlyList<string>> refmapTargets)
    {
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(
                reader.ReadToEnd(),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var package = ReadStringProperty(doc.RootElement, "package");
            var common = ReadMixinNames(doc.RootElement, "mixins");
            if (common.Count == 0)
                return false;

            foreach (var mixin in common)
            {
                if (LooksLikeClientClass(mixin))
                    return true;
                foreach (var key in MixinLookupKeys(package, mixin))
                {
                    if (!refmapTargets.TryGetValue(key, out var targets))
                        continue;
                    foreach (var target in targets)
                    {
                        if (LooksLikeClientClass(target))
                            return true;
                    }
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static List<string> ReadMixinNames(JsonElement root, string property)
    {
        var names = new List<string>();
        if (!TryGetPropertyIgnoreCase(root, property, out var array) || array.ValueKind != JsonValueKind.Array)
            return names;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    names.Add(s.Trim());
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
                continue;
            var env = ReadStringProperty(item, "environment");
            if (env.Equals("client", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = ReadStringProperty(item, "name");
            if (name.Length > 0)
                names.Add(name);
        }

        return names;
    }

    private static IEnumerable<string> MixinLookupKeys(string package, string mixin)
    {
        var leaf = mixin.Replace('\\', '/');
        if (leaf.Contains('/'))
            leaf = leaf[(leaf.LastIndexOf('/') + 1)..];
        var slashPackage = package.Replace('.', '/').Trim('/');
        var fqcn = slashPackage.Length == 0 ? mixin : slashPackage + "/" + mixin.Replace('.', '/');
        yield return NormalizeClassName(mixin);
        yield return NormalizeClassName(leaf);
        yield return NormalizeClassName(fqcn);
        yield return NormalizeClassName(fqcn.Replace('/', '.'));
    }

    private static Dictionary<string, IReadOnlyList<string>> LoadRefmapTargets(ZipArchive jar)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in jar.Entries)
        {
            var path = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (!path.EndsWith(".refmap.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                using var doc = JsonDocument.Parse(
                    reader.ReadToEnd(),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                if (TryGetPropertyIgnoreCase(doc.RootElement, "mappings", out var mappings)
                    && mappings.ValueKind == JsonValueKind.Object)
                {
                    AddRefmapObject(mappings, map);
                }

                if (TryGetPropertyIgnoreCase(doc.RootElement, "data", out var data)
                    && data.ValueKind == JsonValueKind.Object)
                {
                    foreach (var ns in data.EnumerateObject())
                    {
                        if (ns.Value.ValueKind == JsonValueKind.Object)
                            AddRefmapObject(ns.Value, map);
                    }
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddRefmapObject(JsonElement mappings, Dictionary<string, List<string>> map)
    {
        foreach (var mixin in mappings.EnumerateObject())
        {
            if (mixin.Value.ValueKind != JsonValueKind.Object)
                continue;
            var keys = MixinLookupKeys("", mixin.Name).ToList();
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!map.TryGetValue(key, out var targets))
                {
                    targets = [];
                    map[key] = targets;
                }

                foreach (var targetProp in mixin.Value.EnumerateObject())
                {
                    targets.Add(targetProp.Name);
                    if (targetProp.Value.ValueKind == JsonValueKind.String)
                    {
                        var mapped = targetProp.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(mapped))
                            targets.Add(mapped);
                    }
                }
            }
        }
    }

    private static string ReadStringProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return "";
        foreach (var prop in obj.EnumerateObject())
        {
            if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
        }

        return "";
    }

    private static string? ReadMinecraftFromDepends(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        if (!TryGetPropertyIgnoreCase(obj, "depends", out var depends)
            && !TryGetPropertyIgnoreCase(obj, "dependencies", out depends))
            return null;

        if (depends.ValueKind == JsonValueKind.Object)
        {
            var raw = ReadStringOrFirstArrayString(depends, "minecraft");
            return TryExtractMinecraftVersion(raw, out var version) ? version : null;
        }

        if (depends.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in depends.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var id = ReadStringProperty(item, "id");
                if (id.Length == 0)
                    id = ReadStringProperty(item, "modId");
                if (!id.Equals("minecraft", StringComparison.OrdinalIgnoreCase))
                    continue;
                var raw = ReadStringProperty(item, "versions");
                if (raw.Length == 0)
                    raw = ReadStringProperty(item, "version");
                if (TryExtractMinecraftVersion(raw, out var version))
                    return version;
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadStringOrFirstArrayString(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var value))
            return "";
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    return item.GetString() ?? "";
            }
        }

        return "";
    }

    internal static bool TryExtractMinecraftVersion(string? raw, out string version)
    {
        version = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var match = MinecraftVersionRegex.Match(raw);
        if (!match.Success)
            return false;
        version = match.Value;
        return true;
    }
}
