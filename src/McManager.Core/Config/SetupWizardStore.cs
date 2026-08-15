using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Config;

/// <summary>Load/save gitignored Setup wizard resume state. Never writes <c>infra/terraform.tfvars</c>.</summary>
public static class SetupWizardStore
{
    private static readonly JsonSerializerOptions JsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions JsonWrite = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string? GetFilePath(string? dataDirectory = null)
    {
        dataDirectory ??= LocalConfigStore.TryFindDataDirectory();
        return dataDirectory is null
            ? null
            : Path.Combine(dataDirectory, LocalConfigStore.WizardStateFileName);
    }

    public static SetupWizardState LoadOrNew()
    {
        var path = GetFilePath();
        if (path is null || !File.Exists(path))
            return new SetupWizardState();

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<SetupWizardState>(json, JsonRead);
            if (state is null)
                return new SetupWizardState();

            if (state.CurrentStep < 0 || state.CurrentStep >= SetupWizardState.StepCount)
                state.CurrentStep = 0;

            return state;
        }
        catch
        {
            return new SetupWizardState();
        }
    }

    public static ServiceResult Save(SetupWizardState state)
    {
        var dataDir = LocalConfigStore.TryFindDataDirectory();
        if (dataDir is null)
        {
            return ServiceResult.Fail(
                $"Could not locate data directory. Set {LocalConfigStore.ConfigDirEnvVar} or ensure the product repo root is findable.");
        }

        try
        {
            Directory.CreateDirectory(dataDir);
            state.UpdatedAt = DateTime.UtcNow.ToString("o");
            var path = Path.Combine(dataDir, LocalConfigStore.WizardStateFileName);
            var json = JsonSerializer.Serialize(state, JsonWrite);
            File.WriteAllText(path, json);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Failed to save wizard state: {ex.Message}");
        }
    }
}
