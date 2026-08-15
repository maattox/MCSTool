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
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "McManager", "tofu", id);
        Directory.CreateDirectory(root);
        return new TofuWorkspace(root);
    }

    public static string Sanitize(string stackId)
    {
        var chars = stackId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(s) ? DefaultStackId : s;
    }
}
