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
/// Forge/NeoForge <c>displayTest</c> is a connection-screen version check, not a
/// load-side. <c>IGNORE_SERVER_VERSION</c> is used by libraries and server-only mods
/// and must not strip a jar. Explicit client-only markers are <c>clientSideOnly=true</c>
/// and <c>[[mods]] side=CLIENT</c>. Dependency <c>side=BOTH</c> is not the mod's environment.
/// When a mods.toml exists without those client markers, do not strip just because
/// one common mixin <em>targets</em> a client class (CoFH Core). Do strip when a
/// mixin listed in the common <c>mixins</c> array is itself annotated
/// <c>@OnlyIn(Dist.CLIENT)</c> / <c>@Environment(CLIENT)</c> — that class fails
/// FML DistCleaner on a dedicated server (Hold My Items).
/// Mixin target heuristic (no loader toml only, high-confidence): common <c>mixins</c>
/// targets are <strong>exclusively</strong> <see cref="ClientClassPrefixes"/>. Any
/// common mixin targeting a dedicated-safe class keeps the jar. The config's
/// <c>client</c> array is dist-gated and is not a reason to strip. Presence of a
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
                if (hadSide && tomlEnv.Equals("client", StringComparison.OrdinalIgnoreCase))
                    return new PeekResult(true, tomlEnv, loader, tomlMc);
            }

            // Common-list mixin *classes* annotated @OnlyIn(CLIENT) crash FML
            // DistCleaner on dedicated server even when the mixin target is a
            // world class and mods.toml omits clientSideOnly (Hold My Items).
            if (HasClientDistAnnotatedCommonMixin(jar))
                return new PeekResult(true, "client", loader, tomlMc);

            if (hadSide)
                return new PeekResult(true, tomlEnv, loader, tomlMc);

            // Target-class heuristic only when there is no loader toml: a dual-side
            // library may list one client class in the common mixins array (CoFH).
            if (toml is null && HasDedicatedServerKillingMixin(jar))
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

    /// <summary>
    /// Minecraft / loader classes that exist on a dedicated server. Used so a
    /// dual-side jar with one client-class mixin in the common list is kept.
    /// </summary>
    internal static bool LooksLikeDedicatedSafeClass(string? raw)
    {
        if (LooksLikeClientClass(raw))
            return false;
        var normalized = NormalizeClassName(raw);
        if (normalized.Length == 0)
            return false;
        if (normalized.Contains("net/minecraft/", StringComparison.Ordinal))
            return true;
        if (normalized.Contains("net/minecraftforge/", StringComparison.Ordinal))
            return true;
        if (normalized.Contains("net/neoforged/", StringComparison.Ordinal))
            return true;
        if (normalized.Contains("net/fabricmc/", StringComparison.Ordinal))
            return true;
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
                    pendingClientSideOnly = null;
                    continue;
                }

                if (table is null)
                    continue;
                if (TryParseTomlAssignment(line, "side", out var side))
                    pendingSide = side;
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
        var sawClient = false;
        var sawDedicatedSafe = false;
        foreach (var entry in jar.Entries)
        {
            var path = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (!IsMixinConfigPath(path))
                continue;
            ClassifyCommonMixinTargets(entry, refmapTargets, ref sawClient, ref sawDedicatedSafe);
            if (sawDedicatedSafe)
                return false;
        }

        return sawClient && !sawDedicatedSafe;
    }

    private static bool HasClientDistAnnotatedCommonMixin(ZipArchive jar)
    {
        foreach (var entry in jar.Entries)
        {
            var path = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
            if (!IsMixinConfigPath(path))
                continue;
            if (CommonMixinsHaveClientDistClass(jar, entry))
                return true;
        }

        return false;
    }

    private static bool CommonMixinsHaveClientDistClass(ZipArchive jar, ZipArchiveEntry configEntry)
    {
        try
        {
            using var reader = new StreamReader(configEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(
                reader.ReadToEnd(),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var package = ReadStringProperty(doc.RootElement, "package");
            var common = ReadMixinNames(doc.RootElement, "mixins");
            foreach (var mixin in common)
            {
                var classPath = MixinClassEntryPath(package, mixin);
                var classEntry = FindEntry(jar, classPath);
                if (classEntry is null || classEntry.Length <= 0 || classEntry.Length > 512 * 1024)
                    continue;
                using var stream = classEntry.Open();
                var buffer = new byte[classEntry.Length];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0)
                        break;
                    read += n;
                }

                if (read == buffer.Length && ClassFileHasClientDistAnnotation(buffer))
                    return true;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return false;
    }

    internal static string MixinClassEntryPath(string package, string mixin)
    {
        var slashPackage = package.Replace('.', '/').Trim('/');
        var slashMixin = mixin.Replace('.', '/').Replace('\\', '/').Trim('/');
        if (slashPackage.Length == 0)
            return slashMixin + ".class";
        return slashPackage + "/" + slashMixin + ".class";
    }

    internal static bool ClassFileHasClientDistAnnotation(ReadOnlySpan<byte> data)
    {
        if (data.Length < 10
            || data[0] != 0xCA || data[1] != 0xFE || data[2] != 0xBA || data[3] != 0xBE)
            return false;
        try
        {
            var reader = new ClassFileCursor(data);
            reader.Offset = 8;
            var utf8 = reader.ReadConstantPoolUtf8();
            reader.Skip(6);
            var interfaces = reader.ReadU2();
            reader.Skip(interfaces * 2);
            SkipClassMembers(ref reader, reader.ReadU2());
            SkipClassMembers(ref reader, reader.ReadU2());
            var attrCount = reader.ReadU2();
            for (var i = 0; i < attrCount; i++)
            {
                var nameIndex = reader.ReadU2();
                var length = reader.ReadU4();
                var name = Utf8At(utf8, nameIndex);
                var body = reader.ReadBytes(length);
                if (name is "RuntimeVisibleAnnotations" or "RuntimeInvisibleAnnotations"
                    && AnnotationsIncludeClientDist(utf8, body))
                    return true;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return false;
    }

    private static void SkipClassMembers(ref ClassFileCursor reader, int count)
    {
        for (var i = 0; i < count; i++)
        {
            reader.Skip(6);
            var attrCount = reader.ReadU2();
            for (var a = 0; a < attrCount; a++)
            {
                reader.Skip(2);
                reader.Skip(reader.ReadU4());
            }
        }
    }

    private static bool AnnotationsIncludeClientDist(string?[] utf8, ReadOnlySpan<byte> body)
    {
        var reader = new ClassFileCursor(body);
        var count = reader.ReadU2();
        for (var i = 0; i < count; i++)
        {
            if (ReadAnnotationIsClientDist(utf8, ref reader))
                return true;
        }

        return false;
    }

    private static bool ReadAnnotationIsClientDist(string?[] utf8, ref ClassFileCursor reader)
    {
        var type = Utf8At(utf8, reader.ReadU2());
        var pairs = reader.ReadU2();
        var isQuiltClientOnly = type is not null
            && type.Equals("Lorg/quiltmc/loader/api/minecraft/ClientOnly;", StringComparison.Ordinal);
        var isDistAnnotation = isQuiltClientOnly
            || (type is not null
                && (type.Contains("distmarker/OnlyIn;", StringComparison.Ordinal)
                    || type.Equals("Lnet/fabricmc/api/Environment;", StringComparison.Ordinal)));
        var sawClientEnum = false;
        for (var i = 0; i < pairs; i++)
        {
            reader.ReadU2();
            if (ReadElementValueIsClientEnum(utf8, ref reader))
                sawClientEnum = true;
        }

        return isQuiltClientOnly || (isDistAnnotation && sawClientEnum);
    }

    private static bool ReadElementValueIsClientEnum(string?[] utf8, ref ClassFileCursor reader)
    {
        var tag = reader.ReadU1();
        switch (tag)
        {
            case (byte)'B':
            case (byte)'C':
            case (byte)'D':
            case (byte)'F':
            case (byte)'I':
            case (byte)'J':
            case (byte)'S':
            case (byte)'Z':
            case (byte)'s':
            case (byte)'c':
                reader.Skip(2);
                return false;
            case (byte)'e':
            {
                var enumType = Utf8At(utf8, reader.ReadU2());
                var enumName = Utf8At(utf8, reader.ReadU2());
                return enumName is not null
                    && enumName.Equals("CLIENT", StringComparison.Ordinal)
                    && enumType is not null
                    && (enumType.Contains("distmarker/Dist;", StringComparison.Ordinal)
                        || enumType.Equals("Lnet/fabricmc/api/EnvType;", StringComparison.Ordinal));
            }
            case (byte)'@':
                return ReadAnnotationIsClientDist(utf8, ref reader);
            case (byte)'[':
            {
                var n = reader.ReadU2();
                var any = false;
                for (var i = 0; i < n; i++)
                {
                    if (ReadElementValueIsClientEnum(utf8, ref reader))
                        any = true;
                }

                return any;
            }
            default:
                throw new InvalidDataException();
        }
    }

    private static string? Utf8At(string?[] utf8, int index)
    {
        if (index <= 0 || index >= utf8.Length)
            return null;
        return utf8[index];
    }

    private struct ClassFileCursor
    {
        private readonly ReadOnlyMemory<byte> _memory;
        public int Offset;

        public ClassFileCursor(ReadOnlySpan<byte> data)
        {
            _memory = data.ToArray();
            Offset = 0;
        }

        public string?[] ReadConstantPoolUtf8()
        {
            var count = ReadU2();
            var utf8 = new string?[count];
            for (var i = 1; i < count; i++)
            {
                var tag = ReadU1();
                switch (tag)
                {
                    case 1:
                    {
                        var len = ReadU2();
                        utf8[i] = Encoding.UTF8.GetString(ReadBytes(len));
                        break;
                    }
                    case 7:
                    case 8:
                    case 16:
                    case 19:
                    case 20:
                        Skip(2);
                        break;
                    case 3:
                    case 4:
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 17:
                    case 18:
                        Skip(4);
                        break;
                    case 15:
                        Skip(3);
                        break;
                    case 5:
                    case 6:
                        Skip(8);
                        i++;
                        break;
                    default:
                        throw new InvalidDataException();
                }
            }

            return utf8;
        }

        public byte ReadU1()
        {
            var span = Span;
            if (Offset >= span.Length)
                throw new InvalidDataException();
            return span[Offset++];
        }

        public int ReadU2()
        {
            var span = Span;
            if (Offset + 2 > span.Length)
                throw new InvalidDataException();
            var value = (span[Offset] << 8) | span[Offset + 1];
            Offset += 2;
            return value;
        }

        public int ReadU4()
        {
            var span = Span;
            if (Offset + 4 > span.Length)
                throw new InvalidDataException();
            var value = (span[Offset] << 24) | (span[Offset + 1] << 16) | (span[Offset + 2] << 8) | span[Offset + 3];
            Offset += 4;
            if (value < 0)
                throw new InvalidDataException();
            return value;
        }

        public ReadOnlySpan<byte> ReadBytes(int length)
        {
            var span = Span;
            if (length < 0 || Offset + length > span.Length)
                throw new InvalidDataException();
            var slice = span.Slice(Offset, length);
            Offset += length;
            return slice;
        }

        public void Skip(int length)
        {
            if (length < 0 || Offset + length > Span.Length)
                throw new InvalidDataException();
            Offset += length;
        }

        private ReadOnlySpan<byte> Span => _memory.Span;
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

    private static void ClassifyCommonMixinTargets(
        ZipArchiveEntry entry,
        IReadOnlyDictionary<string, IReadOnlyList<string>> refmapTargets,
        ref bool sawClient,
        ref bool sawDedicatedSafe)
    {
        try
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(
                reader.ReadToEnd(),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return;

            var package = ReadStringProperty(doc.RootElement, "package");
            var common = ReadMixinNames(doc.RootElement, "mixins");
            if (common.Count == 0)
                return;

            foreach (var mixin in common)
            {
                NoteTargetClass(mixin, ref sawClient, ref sawDedicatedSafe);
                foreach (var key in MixinLookupKeys(package, mixin))
                {
                    if (!refmapTargets.TryGetValue(key, out var targets))
                        continue;
                    foreach (var target in targets)
                        NoteTargetClass(target, ref sawClient, ref sawDedicatedSafe);
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

    private static void NoteTargetClass(string? raw, ref bool sawClient, ref bool sawDedicatedSafe)
    {
        if (LooksLikeClientClass(raw))
            sawClient = true;
        else if (LooksLikeDedicatedSafeClass(raw))
            sawDedicatedSafe = true;
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
