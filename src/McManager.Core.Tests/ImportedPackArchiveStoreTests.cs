using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ImportedPackArchiveStoreTests
{
    [Fact]
    public void TryFindLatest_returns_null_when_folder_missing()
    {
        var data = NewTempDir();
        try
        {
            Assert.Null(ImportedPackArchiveStore.TryFindLatest(data));
            Assert.Null(ImportedPackArchiveStore.TryFindLatest(null));
            Assert.Empty(ImportedPackArchiveStore.List(""));
        }
        finally
        {
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void Retain_then_TryFindLatest_returns_the_original_archive()
    {
        var data = NewTempDir();
        var source = Path.Combine(data, "Fabulously.mrpack");
        try
        {
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            var retain = ImportedPackArchiveStore.Retain(
                source, "Fabulously Optimized", "5.0", "fabric", "1.21.1", data);
            Assert.True(retain.Succeeded, retain.Error);

            var found = ImportedPackArchiveStore.TryFindLatest(data);
            Assert.NotNull(found);
            Assert.Equal("Fabulously Optimized", found.PackName);
            Assert.Equal("fabric", found.Loader);
            Assert.Equal("Fabulously.mrpack", found.SuggestedDownloadFileName);
            Assert.True(File.Exists(found.ArchivePath));
            Assert.Equal(4, new FileInfo(found.ArchivePath).Length);
            Assert.Equal(
                Path.Combine(data, ImportedPackArchiveStore.DirectoryName),
                Path.GetDirectoryName(Path.GetDirectoryName(found.ArchivePath)));
        }
        finally
        {
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void TryFindLatest_picks_the_newer_sidecar()
    {
        var data = NewTempDir();
        try
        {
            WritePack(data, "old", "old.mrpack", "2020-01-01T00:00:00Z");
            WritePack(data, "new", "new.mrpack", "2026-08-18T00:00:00Z");
            var found = ImportedPackArchiveStore.TryFindLatest(data);
            Assert.NotNull(found);
            Assert.Equal("new", found.PackName);
            Assert.Equal("new.mrpack", found.SuggestedDownloadFileName);
        }
        finally
        {
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void Finds_original_file_when_sidecar_path_is_stale()
    {
        var data = NewTempDir();
        try
        {
            var destDir = Path.Combine(data, ImportedPackArchiveStore.DirectoryName, "moved");
            Directory.CreateDirectory(destDir);
            var archive = Path.Combine(destDir, "original.zip");
            File.WriteAllBytes(archive, [9]);
            File.WriteAllText(
                Path.Combine(destDir, ImportedPackArchiveStore.SidecarFileName),
                """
                {"PackName":"moved","VersionId":null,"Loader":"forge","MinecraftVersion":"1.20.1",
                 "SourceFileName":"pack.zip","RetainedAtUtc":"2026-08-18T00:00:00Z",
                 "ArchivePath":"C:/does-not-exist/original.zip"}
                """);

            var found = ImportedPackArchiveStore.TryFindLatest(data);
            Assert.NotNull(found);
            Assert.Equal(archive, found.ArchivePath);
            Assert.Equal("pack.zip", found.SuggestedDownloadFileName);
            Assert.Equal("forge", found.Loader);
        }
        finally
        {
            TryDeleteDir(data);
        }
    }

    private static void WritePack(string data, string packName, string sourceName, string retainedAt)
    {
        var destDir = ImportedPackArchiveStore.DirectoryFor(data, packName, null);
        Directory.CreateDirectory(destDir);
        var archive = Path.Combine(destDir, "original.mrpack");
        File.WriteAllBytes(archive, [1]);
        File.WriteAllText(
            Path.Combine(destDir, ImportedPackArchiveStore.SidecarFileName),
            $$"""
            {"PackName":"{{packName}}","VersionId":null,"Loader":"fabric","MinecraftVersion":"1.21",
             "SourceFileName":"{{sourceName}}","RetainedAtUtc":"{{retainedAt}}","ArchivePath":{{ToJson(archive)}}}
            """);
    }

    private static string ToJson(string path) =>
        "\"" + path.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-pack-" + Guid.NewGuid().ToString("N"));
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
            // best-effort
        }
    }
}
