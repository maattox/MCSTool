using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FunctionsEventsPurgerTests
{
    [Fact]
    public async Task Empty_compartment_is_noop()
    {
        var api = new FakeApi();
        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(0, result.Value.FunctionsDeleted);
        Assert.Equal(0, result.Value.EventsDeleted);
        Assert.Empty(api.DeletedFunctionIds);
        Assert.Empty(api.DeletedRuleIds);
    }

    [Fact]
    public async Task Deletes_events_rule_then_functions_in_product_app()
    {
        var api = new FakeApi();
        api.Rules.Add(new NamedOciResource("rule-1", FunctionsEventsPurger.ProductEventsRuleName, "ACTIVE"));
        api.Apps.Add(new NamedOciResource("app-1", FunctionsEventsPurger.ProductApplicationName, "ACTIVE"));
        api.FunctionsByApp["app-1"] =
        [
            new NamedOciResource("fn-1", "mcmgr-fn-softstop", "ACTIVE"),
        ];

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value.EventsDeleted);
        Assert.Equal(1, result.Value.FunctionsDeleted);
        Assert.Equal(["rule-1"], api.DeletedRuleIds);
        Assert.Equal(["fn-1"], api.DeletedFunctionIds);
        Assert.True(api.LastRuleDeleteIndex < api.LastFunctionDeleteIndex);
    }

    [Fact]
    public async Task Deletes_every_function_in_the_product_app()
    {
        var api = new FakeApi();
        api.Apps.Add(new NamedOciResource("app-1", FunctionsEventsPurger.ProductApplicationName, "ACTIVE"));
        api.FunctionsByApp["app-1"] =
        [
            new NamedOciResource("fn-1", "mcmgr-fn-softstop", "ACTIVE"),
            new NamedOciResource("fn-2", "other-fill-in", "ACTIVE"),
        ];

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Value.FunctionsDeleted);
        Assert.Equal(["fn-1", "fn-2"], api.DeletedFunctionIds);
    }

    [Fact]
    public async Task Missing_app_still_deletes_events()
    {
        var api = new FakeApi();
        api.Rules.Add(new NamedOciResource("rule-1", FunctionsEventsPurger.ProductEventsRuleName, "ACTIVE"));

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value.EventsDeleted);
        Assert.Equal(0, result.Value.FunctionsDeleted);
        Assert.Equal(["rule-1"], api.DeletedRuleIds);
    }

    [Fact]
    public async Task List_not_found_is_success()
    {
        var api = new FakeApi { ListAppsThrows = new FakeNotFoundException() };

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(0, result.Value.FunctionsDeleted);
    }

    [Fact]
    public async Task Per_function_delete_failure_continues()
    {
        var api = new FakeApi();
        api.Apps.Add(new NamedOciResource("app-1", FunctionsEventsPurger.ProductApplicationName, "ACTIVE"));
        api.FunctionsByApp["app-1"] =
        [
            new NamedOciResource("fn-bad", "broken", "ACTIVE"),
            new NamedOciResource("fn-ok", "mcmgr-fn-softstop", "ACTIVE"),
        ];
        api.FailDeleteFunctionIds.Add("fn-bad");

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value.FunctionsDeleted);
        Assert.Equal(["fn-ok"], api.DeletedFunctionIds);
    }

    [Fact]
    public async Task Waits_until_function_get_returns_gone()
    {
        var api = new FakeApi { GetsUntilGone = 2 };
        api.Apps.Add(new NamedOciResource("app-1", FunctionsEventsPurger.ProductApplicationName, "ACTIVE"));
        api.FunctionsByApp["app-1"] =
        [
            new NamedOciResource("fn-1", "mcmgr-fn-softstop", "ACTIVE"),
        ];

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value.FunctionsDeleted);
        Assert.True(api.GetFunctionCalls >= 2);
    }

    [Fact]
    public async Task Ignores_events_rules_with_other_display_names()
    {
        var api = new FakeApi();
        api.Rules.Add(new NamedOciResource("rule-other", "some-other-rule", "ACTIVE"));
        api.Rules.Add(new NamedOciResource("rule-1", FunctionsEventsPurger.ProductEventsRuleName, "ACTIVE"));

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            api, "ocid1.compartment.oc1..example", log: null, InstantOptions(), CancellationToken.None);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value.EventsDeleted);
        Assert.Equal(["rule-1"], api.DeletedRuleIds);
    }

    private static FunctionsEventsPurgeOptions InstantOptions() =>
        new()
        {
            FunctionGoneTimeout = TimeSpan.FromSeconds(30),
            InitialPollDelay = TimeSpan.FromMilliseconds(1),
            MaxPollDelay = TimeSpan.FromMilliseconds(1),
            DelayAsync = (_, _) => Task.CompletedTask,
        };

    private sealed class FakeNotFoundException : Exception
    {
        public int StatusCode => 404;

        public FakeNotFoundException()
            : base("NotAuthorizedOrNotFound")
        {
        }
    }

    private sealed class FakeApi : IFunctionsEventsApi
    {
        public List<NamedOciResource> Apps { get; } = [];
        public Dictionary<string, List<NamedOciResource>> FunctionsByApp { get; } = new(StringComparer.Ordinal);
        public List<NamedOciResource> Rules { get; } = [];
        public List<string> DeletedFunctionIds { get; } = [];
        public List<string> DeletedRuleIds { get; } = [];
        public HashSet<string> FailDeleteFunctionIds { get; } = new(StringComparer.Ordinal);
        public Exception? ListAppsThrows { get; set; }
        public int GetsUntilGone { get; set; }
        public int GetFunctionCalls { get; private set; }
        public int LastRuleDeleteIndex { get; private set; } = -1;
        public int LastFunctionDeleteIndex { get; private set; } = -1;
        private int _opIndex;

        public Task<IReadOnlyList<NamedOciResource>> ListApplicationsByDisplayNameAsync(
            string compartmentId, string displayName, CancellationToken cancellationToken)
        {
            if (ListAppsThrows is not null)
                throw ListAppsThrows;
            IReadOnlyList<NamedOciResource> items = Apps
                .Where(a => string.Equals(a.DisplayName, displayName, StringComparison.Ordinal))
                .ToList();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<NamedOciResource>> ListFunctionsAsync(
            string applicationId, CancellationToken cancellationToken)
        {
            IReadOnlyList<NamedOciResource> items = FunctionsByApp.TryGetValue(applicationId, out var list)
                ? list
                : [];
            return Task.FromResult(items);
        }

        public Task DeleteFunctionAsync(string functionId, CancellationToken cancellationToken)
        {
            if (FailDeleteFunctionIds.Contains(functionId))
                throw new InvalidOperationException("simulated delete failure");
            DeletedFunctionIds.Add(functionId);
            LastFunctionDeleteIndex = _opIndex++;
            return Task.CompletedTask;
        }

        public Task<NamedOciResource?> GetFunctionAsync(string functionId, CancellationToken cancellationToken)
        {
            GetFunctionCalls++;
            if (GetFunctionCalls <= GetsUntilGone)
                return Task.FromResult<NamedOciResource?>(
                    new NamedOciResource(functionId, "mcmgr-fn-softstop", "DELETING"));
            return Task.FromResult<NamedOciResource?>(null);
        }

        public Task<IReadOnlyList<NamedOciResource>> ListRulesByDisplayNameAsync(
            string compartmentId, string displayName, CancellationToken cancellationToken)
        {
            IReadOnlyList<NamedOciResource> items = Rules
                .Where(r => string.Equals(r.DisplayName, displayName, StringComparison.Ordinal))
                .ToList();
            return Task.FromResult(items);
        }

        public Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
        {
            DeletedRuleIds.Add(ruleId);
            LastRuleDeleteIndex = _opIndex++;
            return Task.CompletedTask;
        }
    }
}
