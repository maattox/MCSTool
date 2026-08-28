using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.Core.Setup;

public readonly record struct FunctionsEventsPurgeResult(int FunctionsDeleted, int EventsDeleted);

internal readonly record struct NamedOciResource(string Id, string? DisplayName, string? LifecycleState);

internal interface IFunctionsEventsApi
{
    Task<IReadOnlyList<NamedOciResource>> ListApplicationsByDisplayNameAsync(
        string compartmentId, string displayName, CancellationToken cancellationToken);

    Task<IReadOnlyList<NamedOciResource>> ListFunctionsAsync(
        string applicationId, CancellationToken cancellationToken);

    Task DeleteFunctionAsync(string functionId, CancellationToken cancellationToken);

    /// <summary>Returns null when the Function is gone (404).</summary>
    Task<NamedOciResource?> GetFunctionAsync(string functionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<NamedOciResource>> ListRulesByDisplayNameAsync(
        string compartmentId, string displayName, CancellationToken cancellationToken);

    Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken);
}

internal sealed class FunctionsEventsPurgeOptions
{
    public TimeSpan FunctionGoneTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialPollDelay { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan MaxPollDelay { get; init; } = TimeSpan.FromSeconds(30);
    public Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }
}

/// <summary>
/// Deletes leftover spend-brake Functions and Events that exist in OCI but may
/// not be in OpenTofu state, so tofu can <c>DeleteApplication</c> on
/// <c>mcmgr-fn-app</c>. Best-effort. Same idea as <see cref="OcirImagePurger"/>.
/// </summary>
public static class FunctionsEventsPurger
{
    public const string ProductApplicationName = "mcmgr-fn-app";
    public const string ProductEventsRuleName = "mcmgr-events-budget-alert";

    public static Task<ServiceResult<FunctionsEventsPurgeResult>> PurgeProductLeftoversAsync(
        OciSession session,
        string compartmentId,
        IProgress<string>? log,
        CancellationToken cancellationToken = default) =>
        PurgeProductLeftoversAsync(
            new OciFunctionsEventsApi(session),
            compartmentId,
            log,
            options: null,
            cancellationToken);

    internal static async Task<ServiceResult<FunctionsEventsPurgeResult>> PurgeProductLeftoversAsync(
        IFunctionsEventsApi api,
        string compartmentId,
        IProgress<string>? log,
        FunctionsEventsPurgeOptions? options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(compartmentId))
            return ServiceResult<FunctionsEventsPurgeResult>.Ok(new FunctionsEventsPurgeResult(0, 0));

        var opts = options ?? new FunctionsEventsPurgeOptions();
        var eventsDeleted = 0;
        var functionsDeleted = 0;

        try
        {
            eventsDeleted = await DeleteProductEventsAsync(api, compartmentId, log, cancellationToken)
                .ConfigureAwait(false);
            functionsDeleted = await DeleteProductFunctionsAsync(
                    api, compartmentId, log, opts, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (OciErrorFormatter.IsNotFound(ex))
                return ServiceResult<FunctionsEventsPurgeResult>.Ok(new FunctionsEventsPurgeResult(functionsDeleted, eventsDeleted));
            return ServiceResult<FunctionsEventsPurgeResult>.Fail(
                ComputeService.FormatOciError("List/Delete Functions or Events", ex));
        }

        return ServiceResult<FunctionsEventsPurgeResult>.Ok(
            new FunctionsEventsPurgeResult(functionsDeleted, eventsDeleted));
    }

    private static async Task<int> DeleteProductEventsAsync(
        IFunctionsEventsApi api,
        string compartmentId,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NamedOciResource> rules;
        try
        {
            rules = await api.ListRulesByDisplayNameAsync(
                compartmentId, ProductEventsRuleName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
                continue;
            if (!string.Equals(rule.DisplayName, ProductEventsRuleName, StringComparison.Ordinal))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                log?.Report($"Deleting Events rule {ProductEventsRuleName} ({ShortId(rule.Id)})…");
                await api.DeleteRuleAsync(rule.Id, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
            catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
            {
                deleted++;
            }
            catch (Exception ex)
            {
                log?.Report(ComputeService.FormatOciError("DeleteRule", ex) + " Continuing.");
            }
        }

        return deleted;
    }

    private static async Task<int> DeleteProductFunctionsAsync(
        IFunctionsEventsApi api,
        string compartmentId,
        IProgress<string>? log,
        FunctionsEventsPurgeOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NamedOciResource> apps;
        try
        {
            apps = await api.ListApplicationsByDisplayNameAsync(
                compartmentId, ProductApplicationName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
        {
            return 0;
        }

        var matching = apps
            .Where(a =>
                !string.IsNullOrWhiteSpace(a.Id)
                && string.Equals(a.DisplayName, ProductApplicationName, StringComparison.Ordinal))
            .ToList();
        if (matching.Count == 0)
        {
            log?.Report($"No Functions application named {ProductApplicationName} in the stack compartment.");
            return 0;
        }

        var deleted = 0;
        foreach (var app in matching)
        {
            IReadOnlyList<NamedOciResource> functions;
            try
            {
                functions = await api.ListFunctionsAsync(app.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
            {
                continue;
            }
            catch (Exception ex)
            {
                log?.Report(ComputeService.FormatOciError("ListFunctions", ex) + " Continuing.");
                continue;
            }

            foreach (var fn in functions)
            {
                if (string.IsNullOrWhiteSpace(fn.Id))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                var name = string.IsNullOrWhiteSpace(fn.DisplayName) ? ShortId(fn.Id) : fn.DisplayName;
                try
                {
                    if (!IsGone(fn.LifecycleState))
                    {
                        log?.Report($"Deleting Function {name} ({ShortId(fn.Id)})…");
                        await api.DeleteFunctionAsync(fn.Id, cancellationToken).ConfigureAwait(false);
                    }

                    var gone = await WaitUntilFunctionGoneAsync(
                        api, fn.Id, name, log, options, cancellationToken).ConfigureAwait(false);
                    if (gone)
                        deleted++;
                    else
                    {
                        log?.Report(
                            $"Function {name} is still present after delete. Continuing; tofu destroy may fail on mcmgr-fn-app.");
                    }
                }
                catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
                {
                    deleted++;
                }
                catch (Exception ex)
                {
                    log?.Report(ComputeService.FormatOciError("DeleteFunction", ex) + " Continuing.");
                }
            }
        }

        return deleted;
    }

    private static async Task<bool> WaitUntilFunctionGoneAsync(
        IFunctionsEventsApi api,
        string functionId,
        string name,
        IProgress<string>? log,
        FunctionsEventsPurgeOptions options,
        CancellationToken cancellationToken)
    {
        var delay = options.DelayAsync ?? ((d, ct) => Task.Delay(d, ct));
        var deadline = DateTime.UtcNow + options.FunctionGoneTimeout;
        var delaySeconds = options.InitialPollDelay.TotalSeconds;
        var maxDelaySeconds = options.MaxPollDelay.TotalSeconds;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NamedOciResource? current;
            try
            {
                current = await api.GetFunctionAsync(functionId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
            {
                return true;
            }

            if (current is null || IsGone(current.Value.LifecycleState))
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            log?.Report($"Waiting for Function {name} to finish deleting…");
            var wait = TimeSpan.FromSeconds(Math.Min(delaySeconds, maxDelaySeconds));
            await delay(wait, cancellationToken).ConfigureAwait(false);
            delaySeconds = Math.Min(delaySeconds * 2, maxDelaySeconds);
        }
    }

    private static bool IsGone(string? lifecycleState) =>
        string.Equals(lifecycleState, "DELETED", StringComparison.OrdinalIgnoreCase);

    private static string ShortId(string ocid)
    {
        if (ocid.Length <= 22)
            return ocid;
        return ocid[^12..];
    }
}
