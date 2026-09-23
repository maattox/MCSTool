using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>Build derived unstructured packs and retain for Download pack (Step 8.8 P9).</summary>
public static class DerivedPackWorkflow
{
    public static ServiceResult<string> BuildAndRetain(
        string sourceZipPath,
        string packName,
        string? versionId,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        string javaMajorText,
        string dataDirectory,
        string? originalFileName = null)
    {
        if (!DerivedPackIdentity.TryNormalizeMinecraft(minecraftVersion, out var mc))
            return ServiceResult<string>.Fail(DerivedPackIdentity.IdentityIncompleteReason);
        if (!DerivedPackIdentity.TryNormalizeLoader(loader, out var loaderId))
            return ServiceResult<string>.Fail(DerivedPackIdentity.IdentityIncompleteReason);
        if (!DerivedPackIdentity.TryNormalizeLoaderVersion(loaderVersion, out var loaderVer))
            return ServiceResult<string>.Fail(DerivedPackIdentity.IdentityIncompleteReason);
        if (!DerivedPackIdentity.TryNormalizeJavaMajor(javaMajorText, out var javaMajor))
            return ServiceResult<string>.Fail(DerivedPackIdentity.IdentityIncompleteReason);

        var analysisResult = ManualServerPackAnalyzer.AnalyzeFile(sourceZipPath);
        if (!analysisResult.Succeeded || analysisResult.Value is null)
            return ServiceResult<string>.Fail(analysisResult.Error ?? "Could not check the modpack.");

        var analysis = analysisResult.Value;
        if (!DerivedPackIdentity.NeedsIdentityConfirm(analysis.Kind))
        {
            return ServiceResult<string>.Fail("This modpack doesn't need version fixes.");
        }

        var fields = new DerivedPackFields(mc, loaderId, loaderVer, javaMajor);
        var build = DerivedPackArchive.BuildIntoDataDirectory(
            sourceZipPath,
            analysis,
            fields,
            dataDirectory,
            originalFileName ?? Path.GetFileName(sourceZipPath));
        if (!build.Succeeded || string.IsNullOrWhiteSpace(build.Value))
            return ServiceResult<string>.Fail(build.Error ?? "Could not apply your version fixes to the modpack.");

        var retain = ImportedPackArchiveStore.Retain(
            build.Value,
            packName,
            versionId,
            loaderId,
            mc,
            dataDirectory);
        if (!retain.Succeeded)
            return ServiceResult<string>.Fail(retain.Error ?? "Could not save the modpack with your version fixes.");

        return ServiceResult<string>.Ok(build.Value);
    }
}
