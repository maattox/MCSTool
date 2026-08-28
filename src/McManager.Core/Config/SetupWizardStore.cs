using System.Text.Json;
using McManager.Core.Services;
using McManager.Core.Setup;

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
            return Normalize(new SetupWizardState());

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<SetupWizardState>(json, JsonRead);
            if (state is null)
                return Normalize(new SetupWizardState());

            return Normalize(state);
        }
        catch
        {
            return Normalize(new SetupWizardState());
        }
    }

    /// <summary>
    /// Bump schema, remap step indexes after the Compartment page was removed,
    /// Name and icon was inserted, and budget email merged into OCI, and force
    /// create-compartment (paste-OCID is Advanced Auto-detect only).
    /// </summary>
    public static SetupWizardState Normalize(SetupWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.SchemaVersion < SetupWizardState.CurrentSchemaVersion)
        {
            if (state.SchemaVersion <= 1)
                state.CurrentStep = MigrateStepIndexFromV1(state.CurrentStep);
            if (state.SchemaVersion <= 2)
                state.CurrentStep = MigrateStepIndexFromV2(state.CurrentStep);
            if (state.SchemaVersion <= 3)
                state.CurrentStep = MigrateStepIndexFromV3(state.CurrentStep);
            state.SchemaVersion = SetupWizardState.CurrentSchemaVersion;
        }

        state.CreateCompartment = true;
        state.ExistingCompartmentId = "";

        var name = state.CompartmentName?.Trim() ?? "";
        var hasTofu = !string.IsNullOrWhiteSpace(name)
                      && TofuWorkspace.TryFindExisting(name) is not null;
        if (!hasTofu && !CompartmentNamer.IsProductName(name))
            state.CompartmentName = CompartmentNamer.BaseName;
        else
            state.CompartmentName = string.IsNullOrWhiteSpace(name) ? CompartmentNamer.BaseName : name;

        if (state.CurrentStep < 0 || state.CurrentStep >= SetupWizardState.StepCount)
            state.CurrentStep = 0;

        return state;
    }

    /// <summary>
    /// v1: 0 Always Free, 1 OCI, 2 Compartment, 3 email … 8 Review.
    /// v2: 0 Always Free, 1 OCI, 2 email … 7 Review.
    /// On the deleted Compartment page, land on email (v2/v3 index 2; v4 OCI page).
    /// </summary>
    public static int MigrateStepIndexFromV1(int oldStep) =>
        oldStep > 2 ? oldStep - 1 : oldStep;

    /// <summary>
    /// v2: 0 Always Free … 4 Game, 5 EULA, 6 Auth, 7 Review.
    /// v3: inserts Name and icon at 5; EULA and later shift +1.
    /// </summary>
    public static int MigrateStepIndexFromV2(int oldStep) =>
        oldStep >= 5 ? oldStep + 1 : oldStep;

    /// <summary>
    /// v3: 0 Always Free, 1 OCI, 2 email, 3 SSH … 8 Review.
    /// v4: email merges into OCI; SSH and later shift −1.
    /// A saved email page (index 2) lands on the combined OCI page (index 1).
    /// </summary>
    public static int MigrateStepIndexFromV3(int oldStep) =>
        oldStep >= 2 ? oldStep - 1 : oldStep;

    public static ServiceResult Save(SetupWizardState state)
    {
        var dataDir = LocalConfigStore.TryFindDataDirectory();
        if (dataDir is null)
            return ServiceResult.Fail(LocalConfigStore.CannotWriteSettingsMessage);

        try
        {
            Directory.CreateDirectory(dataDir);
            state.SchemaVersion = SetupWizardState.CurrentSchemaVersion;
            state.UpdatedAt = DateTime.UtcNow.ToString("o");
            var path = Path.Combine(dataDir, LocalConfigStore.WizardStateFileName);
            var json = JsonSerializer.Serialize(state, JsonWrite);
            File.WriteAllText(path, json);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Could not write Manager settings: {ex.Message}");
        }
    }
}
