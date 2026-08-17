namespace McManager.Core.Setup;

/// <summary>
/// Gitignored OpenTofu working files on the admin PC.
/// Never the product bucket and never <c>infra/terraform.tfvars</c> in the repo.
/// </summary>
public sealed class TofuWorkspace
{
    public const string DefaultStackId = "mcmgr";

    public TofuWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
        VarFilePath = Path.Combine(rootDirectory, "terraform.tfvars");
        StatePath = Path.Combine(rootDirectory, "terraform.tfstate");
        OutputsPath = Path.Combine(rootDirectory, "outputs.json");
    }

    public string RootDirectory { get; }
    public string VarFilePath { get; }
    public string StatePath { get; }
    public string OutputsPath { get; }

    public static TofuWorkspace ForStack(string? stackId = null)
    {
        var id = Sanitize(string.IsNullOrWhiteSpace(stackId) ? DefaultStackId : stackId);
        var root = Path.Combine(TofuRootDirectory(), id);
        Directory.CreateDirectory(root);
        return new TofuWorkspace(root);
    }

    public static string TofuRootDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "McManager", "tofu");
    }

    public bool HasState => File.Exists(StatePath);

    public string StackId => Path.GetFileName(RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Existing Setup workspaces that have a state file. Does not create directories.
    /// </summary>
    public static IReadOnlyList<TofuWorkspace> ListExisting()
    {
        var root = TofuRootDirectory();
        if (!Directory.Exists(root))
            return [];

        var list = new List<TofuWorkspace>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var ws = new TofuWorkspace(dir);
            if (ws.HasState)
                list.Add(ws);
        }

        return list;
    }

    public static TofuWorkspace? TryFindExisting(string? stackId)
    {
        var id = Sanitize(string.IsNullOrWhiteSpace(stackId) ? DefaultStackId : stackId);
        var root = Path.Combine(TofuRootDirectory(), id);
        if (!Directory.Exists(root))
            return null;
        var ws = new TofuWorkspace(root);
        return ws.HasState ? ws : null;
    }

    public static string Sanitize(string stackId)
    {
        var chars = stackId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(s) ? DefaultStackId : s;
    }
}
