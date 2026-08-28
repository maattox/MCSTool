using McManager.Core.Oci;
using McManager.Core.Services;
using Oci.EventsService.Requests;
using Oci.FunctionsService.Requests;

namespace McManager.Core.Setup;

internal sealed class OciFunctionsEventsApi : IFunctionsEventsApi
{
    private const int PageLimit = 50;
    private readonly OciSession _session;

    public OciFunctionsEventsApi(OciSession session) => _session = session;

    public async Task<IReadOnlyList<NamedOciResource>> ListApplicationsByDisplayNameAsync(
        string compartmentId, string displayName, CancellationToken cancellationToken)
    {
        var list = new List<NamedOciResource>();
        string? page = null;
        do
        {
            var response = await _session.Functions.ListApplications(
                new ListApplicationsRequest
                {
                    CompartmentId = compartmentId,
                    DisplayName = displayName,
                    Limit = PageLimit,
                    Page = page,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        continue;
                    list.Add(new NamedOciResource(
                        item.Id, item.DisplayName, item.LifecycleState?.ToString()));
                }
            }

            page = response.OpcNextPage;
        }
        while (!string.IsNullOrWhiteSpace(page));

        return list;
    }

    public async Task<IReadOnlyList<NamedOciResource>> ListFunctionsAsync(
        string applicationId, CancellationToken cancellationToken)
    {
        var list = new List<NamedOciResource>();
        string? page = null;
        do
        {
            var response = await _session.Functions.ListFunctions(
                new ListFunctionsRequest
                {
                    ApplicationId = applicationId,
                    Limit = PageLimit,
                    Page = page,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        continue;
                    list.Add(new NamedOciResource(
                        item.Id, item.DisplayName, item.LifecycleState?.ToString()));
                }
            }

            page = response.OpcNextPage;
        }
        while (!string.IsNullOrWhiteSpace(page));

        return list;
    }

    public Task DeleteFunctionAsync(string functionId, CancellationToken cancellationToken) =>
        _session.Functions.DeleteFunction(
            new DeleteFunctionRequest { FunctionId = functionId },
            cancellationToken: cancellationToken);

    public async Task<NamedOciResource?> GetFunctionAsync(
        string functionId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _session.Functions.GetFunction(
                new GetFunctionRequest { FunctionId = functionId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var fn = response.Function;
            if (fn is null || string.IsNullOrWhiteSpace(fn.Id))
                return null;
            return new NamedOciResource(fn.Id, fn.DisplayName, fn.LifecycleState?.ToString());
        }
        catch (Exception ex) when (OciErrorFormatter.IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<NamedOciResource>> ListRulesByDisplayNameAsync(
        string compartmentId, string displayName, CancellationToken cancellationToken)
    {
        var list = new List<NamedOciResource>();
        string? page = null;
        do
        {
            var response = await _session.Events.ListRules(
                new ListRulesRequest
                {
                    CompartmentId = compartmentId,
                    DisplayName = displayName,
                    Limit = PageLimit,
                    Page = page,
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Id))
                        continue;
                    if (!string.Equals(item.DisplayName, displayName, StringComparison.Ordinal))
                        continue;
                    list.Add(new NamedOciResource(
                        item.Id, item.DisplayName, item.LifecycleState?.ToString()));
                }
            }

            page = response.OpcNextPage;
        }
        while (!string.IsNullOrWhiteSpace(page));

        return list;
    }

    public Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken) =>
        _session.Events.DeleteRule(
            new DeleteRuleRequest { RuleId = ruleId },
            cancellationToken: cancellationToken);
}
