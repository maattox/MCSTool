namespace McManager.PackTestHarness;

internal sealed class PackCatalog
{
    public int SchemaVersion { get; init; } = 1;
    public IReadOnlyList<CatalogPack> Packs { get; init; } = [];

    public CatalogPack? Find(string id) =>
        Packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static PackCatalog Load(string path)
    {
        var text = File.ReadAllText(path);
        return Parse(text);
    }

    public static PackCatalog Parse(string text)
    {
        var packs = new List<CatalogPack>();
        var schema = 1;
        CatalogPackBuilder? current = null;
        var inPacks = false;

        foreach (var raw in (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var stripped = StripComment(raw);
            if (string.IsNullOrWhiteSpace(stripped))
                continue;

            var indent = Indent(stripped);
            var line = stripped.Trim();

            if (indent == 0 && line.StartsWith("schema_version:", StringComparison.Ordinal))
            {
                _ = int.TryParse(YamlText.Unquote(AfterColon(line)), out schema);
                continue;
            }

            if (indent == 0 && line.StartsWith("packs:", StringComparison.Ordinal))
            {
                Flush(packs, ref current);
                inPacks = true;
                var rest = AfterColon(line).Trim();
                if (rest == "[]")
                    inPacks = false;
                continue;
            }

            if (!inPacks)
                continue;

            if (line.StartsWith("- ", StringComparison.Ordinal) || line == "-")
            {
                Flush(packs, ref current);
                current = new CatalogPackBuilder();
                var rest = line.Length <= 2 ? "" : line[2..].Trim();
                if (rest.Length > 0)
                    ApplyField(current, rest);
                continue;
            }

            if (current is not null && indent >= 2)
                ApplyField(current, line);
        }

        Flush(packs, ref current);
        return new PackCatalog { SchemaVersion = schema, Packs = packs };
    }

    private static void Flush(List<CatalogPack> packs, ref CatalogPackBuilder? current)
    {
        if (current is null)
            return;
        if (!string.IsNullOrWhiteSpace(current.Id))
            packs.Add(current.Build());
        current = null;
    }

    private static void ApplyField(CatalogPackBuilder current, string line)
    {
        var colon = line.IndexOf(':');
        if (colon <= 0)
            return;
        var key = line[..colon].Trim();
        var value = YamlText.Unquote(line[(colon + 1)..]);
        switch (key)
        {
            case "id": current.Id = value; break;
            case "filename": current.Filename = value; break;
            case "sha256": current.Sha256 = value; break;
            case "platform": current.Platform = value; break;
            case "format": current.Format = value; break;
            case "loader": current.Loader = value; break;
            case "loader_version": current.LoaderVersion = value; break;
            case "minecraft": current.Minecraft = value; break;
            case "java_major":
                _ = int.TryParse(value, out var j);
                current.JavaMajor = j;
                break;
            case "size_class": current.SizeClass = value; break;
            case "client_only_sidecar": current.ClientOnlySidecar = value; break;
        }
    }

    private static string AfterColon(string line)
    {
        var colon = line.IndexOf(':');
        return colon < 0 ? "" : line[(colon + 1)..];
    }

    private static int Indent(string line)
    {
        var n = 0;
        while (n < line.Length && line[n] == ' ')
            n++;
        return n;
    }

    private static string StripComment(string line)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && !inSingle)
                inDouble = !inDouble;
            else if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '#' && !inSingle && !inDouble)
                return line[..i];
        }

        return line;
    }

    private sealed class CatalogPackBuilder
    {
        public string Id { get; set; } = "";
        public string Filename { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Format { get; set; } = "";
        public string Loader { get; set; } = "";
        public string LoaderVersion { get; set; } = "";
        public string Minecraft { get; set; } = "";
        public int JavaMajor { get; set; }
        public string SizeClass { get; set; } = "";
        public string ClientOnlySidecar { get; set; } = "";

        public CatalogPack Build() => new()
        {
            Id = Id.Trim(),
            Filename = Filename.Trim(),
            Sha256 = NormalizeSha(Sha256),
            Platform = Platform.Trim(),
            Format = Format.Trim(),
            Loader = Loader.Trim(),
            LoaderVersion = LoaderVersion.Trim(),
            Minecraft = Minecraft.Trim(),
            JavaMajor = JavaMajor,
            SizeClass = SizeClass.Trim(),
            ClientOnlySidecar = ClientOnlySidecar.Trim(),
        };
    }

    internal static string NormalizeSha(string? value)
    {
        var t = (value ?? "").Trim();
        if (t.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            t = t["sha256:".Length..].Trim();
        return t.ToLowerInvariant();
    }
}

internal sealed class CatalogPack
{
    public required string Id { get; init; }
    public string Filename { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Platform { get; init; } = "";
    public string Format { get; init; } = "";
    public string Loader { get; init; } = "";
    public string LoaderVersion { get; init; } = "";
    public string Minecraft { get; init; } = "";
    public int JavaMajor { get; init; }
    public string SizeClass { get; init; } = "";
    public string ClientOnlySidecar { get; init; } = "";

    public bool HasSha256 => Sha256.Length > 0;
}
