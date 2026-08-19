using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// Pull/publish shared usage ledger + budget config with <c>meta/flags.json</c> dirty protocol.
/// </summary>
public sealed class UsageBudgetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly IObjectStorageService _objectStorage;
    private readonly ObjectStoragePrefixes _prefixes;

    public UsageBudgetStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _prefixes = prefixes;
    }

    public string FlagsObjectName => Combine(_prefixes.Meta, "flags.json");
    public string BudgetObjectName => Combine(_prefixes.Budget, "config.json");
    public string LedgerObjectName => Combine(_prefixes.Ledger, "usage.json");

    public async Task<ServiceResult<UsagePullSnapshot>> PullAsync(
        bool forceLedger,
        UsageLedgerDocument? previousLedger = null,
        CancellationToken cancellationToken = default)
    {
        var flagsResult = await GetJsonAsync<MetaFlagsDocument>(FlagsObjectName, cancellationToken);
        MetaFlagsDocument flags;
        string? flagsEtag = null;
        var flagsExisted = false;
        if (flagsResult.Succeeded && flagsResult.Value is not null)
        {
            flags = flagsResult.Value.Document;
            flagsEtag = flagsResult.Value.Etag;
            flagsExisted = true;
            flags.Normalize();
        }
        else if (OciErrorFormatter.IsNotFoundMessage(flagsResult.Error))
        {
            flags = MetaFlagsDocument.Empty();
        }
        else
        {
            return ServiceResult<UsagePullSnapshot>.Fail(flagsResult.Error ?? "Failed to get flags.");
        }

        var budgetResult = await GetJsonAsync<BudgetConfigDocument>(BudgetObjectName, cancellationToken);
        BudgetConfigDocument? budget = null;
        string? budgetNote = null;
        if (budgetResult.Succeeded && budgetResult.Value is not null)
        {
            budget = budgetResult.Value.Document;
        }
        else if (OciErrorFormatter.IsNotFoundMessage(budgetResult.Error))
        {
            budgetNote = "budget/config.json missing — using local config fallback.";
        }
        else
        {
            return ServiceResult<UsagePullSnapshot>.Fail(budgetResult.Error ?? "Failed to get budget.");
        }

        var ledgerDirty = flags.IsDirty("ledger", "manager");
        var shouldPullLedger = forceLedger || ledgerDirty;
        UsageLedgerDocument ledger;
        var ledgerPulled = false;
        string? ledgerNote = null;

        if (shouldPullLedger)
        {
            var ledgerResult = await GetJsonAsync<UsageLedgerDocument>(LedgerObjectName, cancellationToken);
            if (ledgerResult.Succeeded && ledgerResult.Value is not null)
            {
                ledger = ledgerResult.Value.Document;
                ledger.DailyOverrides ??= new Dictionary<string, DailyOverride>(StringComparer.Ordinal);
                ledger.Intervals ??= [];
                ledgerPulled = true;

                if (ledgerDirty || forceLedger)
                {
                    var require = ObjectStorageConditional.RequireEtagIfPresent(
                        FlagsObjectName, flagsExisted, flagsEtag);
                    if (!require.Succeeded)
                    {
                        return ServiceResult<UsagePullSnapshot>.Fail(
                            require.Error ?? ObjectStorageConflict.MissingEtag(FlagsObjectName));
                    }

                    flags.ClearFlag("ledger", "manager");
                    var putFlags = await PutJsonAsync(
                        FlagsObjectName, flags, flagsEtag, cancellationToken);
                    if (!putFlags.Succeeded)
                    {
                        return ServiceResult<UsagePullSnapshot>.Fail(
                            putFlags.Error ?? "Pulled ledger but failed to clear manager flag.");
                    }
                }

                ledgerNote = forceLedger && !ledgerDirty
                    ? $"Forced ledger pull ({ledger.Intervals.Count} intervals)."
                    : $"Pulled ledger ({ledger.Intervals.Count} intervals); cleared manager ledger flag.";
            }
            else if (OciErrorFormatter.IsNotFoundMessage(ledgerResult.Error))
            {
                ledger = previousLedger ?? UsageLedgerDocument.Empty();
                ledgerNote = "ledger/usage.json missing — empty ledger.";
            }
            else
            {
                return ServiceResult<UsagePullSnapshot>.Fail(ledgerResult.Error ?? "Failed to get ledger.");
            }
        }
        else
        {
            ledger = previousLedger ?? UsageLedgerDocument.Empty();
            ledgerNote =
                $"Ledger flag clear — kept cached ledger ({ledger.Intervals.Count} intervals).";
        }

        return ServiceResult<UsagePullSnapshot>.Ok(new UsagePullSnapshot
        {
            Flags = flags,
            Budget = budget,
            Ledger = ledger,
            LedgerPulled = ledgerPulled,
            BudgetMissing = budget is null,
            Notes = string.Join(" ", new[] { budgetNote, ledgerNote }.Where(s => !string.IsNullOrWhiteSpace(s))),
        });
    }

    public async Task<ServiceResult<UsagePublishResult>> PublishBudgetAsync(
        BudgetConfigDocument document,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetJsonAsync<BudgetConfigDocument>(BudgetObjectName, cancellationToken);
        string? budgetEtag = null;
        if (existing.Succeeded && existing.Value is not null)
        {
            budgetEtag = existing.Value.Etag;
            var require = ObjectStorageConditional.RequireEtagIfPresent(
                BudgetObjectName, objectExists: true, budgetEtag);
            if (!require.Succeeded)
                return ServiceResult<UsagePublishResult>.Fail(
                    require.Error ?? ObjectStorageConflict.MissingEtag(BudgetObjectName));
        }
        else if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
        {
            return ServiceResult<UsagePublishResult>.Fail(existing.Error ?? "Failed to get budget.");
        }

        document.StampUpdated();
        var putBudget = await PutJsonAsync(BudgetObjectName, document, budgetEtag, cancellationToken);
        if (!putBudget.Succeeded)
            return ServiceResult<UsagePublishResult>.Fail(putBudget.Error ?? "Put budget failed.");

        var flagsResult = await GetJsonAsync<MetaFlagsDocument>(FlagsObjectName, cancellationToken);
        MetaFlagsDocument flags;
        string? flagsEtag = null;
        if (flagsResult.Succeeded && flagsResult.Value is not null)
        {
            flags = flagsResult.Value.Document;
            flagsEtag = flagsResult.Value.Etag;
            flags.Normalize();
            var requireFlags = ObjectStorageConditional.RequireEtagIfPresent(
                FlagsObjectName, objectExists: true, flagsEtag);
            if (!requireFlags.Succeeded)
            {
                return ServiceResult<UsagePublishResult>.Fail(
                    requireFlags.Error ?? ObjectStorageConflict.MissingEtag(FlagsObjectName));
            }
        }
        else if (OciErrorFormatter.IsNotFoundMessage(flagsResult.Error))
        {
            flags = MetaFlagsDocument.Empty();
        }
        else
        {
            return ServiceResult<UsagePublishResult>.Fail(
                flagsResult.Error ?? "Budget saved but failed to load flags.");
        }

        flags.MarkDirty("budget", ["door", "vm1"], clearWriter: "manager");
        var putFlags = await PutJsonAsync(FlagsObjectName, flags, flagsEtag, cancellationToken);
        if (!putFlags.Succeeded)
        {
            return ServiceResult<UsagePublishResult>.Fail(
                putFlags.Error ?? "Budget saved but failed to update flags.");
        }

        return ServiceResult<UsagePublishResult>.Ok(new UsagePublishResult
        {
            Budget = document,
            Flags = flags,
            Message =
                $"Published {BudgetObjectName}; set budget flags door=true, vm1=true; manager=false.",
        });
    }

    /// <summary>
    /// First-deploy seed so door <c>pull_os_budget.sh --force</c> does not 404 on an empty bucket.
    /// Does not overwrite a ledger VM1 already published.
    /// </summary>
    public async Task<ServiceResult> SeedEmptyLedgerIfMissingAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await GetJsonAsync<UsageLedgerDocument>(LedgerObjectName, cancellationToken);
        if (existing.Succeeded && existing.Value is not null)
            return ServiceResult.Ok();

        if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
            return ServiceResult.Fail(existing.Error ?? "Get ledger failed.");

        var empty = UsageLedgerDocument.Empty();
        var put = await PutJsonAsync(LedgerObjectName, empty, ifMatch: null, cancellationToken);
        return put.Succeeded
            ? ServiceResult.Ok()
            : ServiceResult.Fail(put.Error ?? "Put empty ledger failed.");
    }

    private sealed class JsonGet<T> where T : class
    {
        public required T Document { get; init; }
        public string? Etag { get; init; }
    }

    private async Task<ServiceResult<JsonGet<T>>> GetJsonAsync<T>(
        string objectName,
        CancellationToken cancellationToken)
        where T : class
    {
        var got = await _objectStorage.GetObjectAsync(objectName, cancellationToken);
        if (!got.Succeeded || got.Value is null)
            return ServiceResult<JsonGet<T>>.Fail(got.Error ?? $"GetObject {objectName} failed.");

        try
        {
            var doc = JsonSerializer.Deserialize<T>(got.Value.Content, JsonOptions);
            if (doc is null)
                return ServiceResult<JsonGet<T>>.Fail($"{objectName} is empty or invalid JSON.");
            return ServiceResult<JsonGet<T>>.Ok(new JsonGet<T>
            {
                Document = doc,
                Etag = got.Value.Etag,
            });
        }
        catch (JsonException ex)
        {
            return ServiceResult<JsonGet<T>>.Fail($"{objectName} JSON parse failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult> PutJsonAsync<T>(
        string objectName,
        T document,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!json.EndsWith('\n'))
        {
            var withNl = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, withNl, 0, bytes.Length);
            withNl[^1] = (byte)'\n';
            bytes = withNl;
        }

        return await _objectStorage.PutBytesAsync(
            objectName,
            bytes,
            "application/json",
            ifMatch,
            cancellationToken);
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class UsagePullSnapshot
{
    public required MetaFlagsDocument Flags { get; init; }
    public BudgetConfigDocument? Budget { get; init; }
    public required UsageLedgerDocument Ledger { get; init; }
    public bool LedgerPulled { get; init; }
    public bool BudgetMissing { get; init; }
    public string Notes { get; init; } = "";
}

public sealed class UsagePublishResult
{
    public required BudgetConfigDocument Budget { get; init; }
    public required MetaFlagsDocument Flags { get; init; }
    public required string Message { get; init; }
}
