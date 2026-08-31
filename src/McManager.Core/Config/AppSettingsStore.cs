using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Config;

/// <summary>
/// Load/save <see cref="AppSettingsDocument"/> on the admin PC.
/// Missing or malformed files return defaults (update-check on).
/// </summary>
public static class AppSettingsStore
{
    public const string FileName = "app-settings.json";
    public const string ProductFolderName = "MCSTool";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    public static string DefaultFilePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, ProductFolderName, FileName);
    }

    public static AppSettingsDocument Load(string? filePath = null)
    {
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath() : filePath;
        if (!File.Exists(path))
            return AppSettingsDocument.Default();

        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<AppSettingsDocument>(json, JsonOptions);
            return doc ?? AppSettingsDocument.Default();
        }
        catch (Exception)
        {
            return AppSettingsDocument.Default();
        }
    }

    public static ServiceResult Save(AppSettingsDocument document, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var path = string.IsNullOrWhiteSpace(filePath) ? DefaultFilePath() : filePath;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            document.Version = AppSettingsDocument.DocumentVersion;
            var json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(path, json);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to save program settings: {ex.Message}");
        }
    }
}
