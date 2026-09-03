using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the alert RULE list (/operate/alerts/rules) and rule detail / condition
/// editor (/operate/alerts/rules/{ruleId}) — UI-042, honua-server#1169. Asserts the merged-build
/// missing-binding state (no fabricated rules) plus the bound list and condition editor. Drives the pages
/// through a fake IOperateAlertRulesDataSource (never a mock server). The merged-build
/// UnsupportedOperateAlertRulesDataSource is exercised through real DI.
/// </summary>
public sealed class OperateAlertRulesPageRenderTests
{
    private static readonly OperateAlertRulesBindingState MissingBinding = new(
        "Alert rules", OperateAlertRulesBindingState.MissingBinding, "honua-server#1169",
        "Alert rule definitions bind to honua-server#1169.");

    [Fact]
    public void RulesList_WhenBindingMissing_RendersNotBoundSurface()
    {
        var page = RenderList(new FakeRulesDataSource { ListView = new OperateAlertRulesView([], MissingBinding) });

        page.WaitForAssertion(
            () => Assert.Contains("Alert rules are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("honua-server#1169", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RulesList_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateAlertRulesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Alert rules are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RulesList_WhenBound_RendersRuleRows()
    {
        var page = RenderList(new FakeRulesDataSource { ListView = new OperateAlertRulesView([SampleRule]) });

        page.WaitForAssertion(
            () => Assert.Contains("data-alert-rule=\"rule-1\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Speeding geofence", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-alert-rule-create", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Alert rules are not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleDetail_WhenBindingMissing_RendersNotBoundSurface()
    {
        var page = RenderDetail(
            new FakeRulesDataSource { DetailView = new OperateAlertRuleDetailView(null, MissingBinding) },
            "rule-1");

        page.WaitForAssertion(
            () => Assert.Contains("Alert rule editor is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-alert-rule-editor", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleDetail_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateAlertRuleDetailPage>(parameters =>
            parameters.Add(p => p.RuleId, "rule-1"));

        page.WaitForAssertion(
            () => Assert.Contains("Alert rule editor is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RuleDetail_WhenBound_RendersConditionEditor()
    {
        var page = RenderDetail(
            new FakeRulesDataSource { DetailView = new OperateAlertRuleDetailView(SampleDefinition) },
            "rule-1");

        page.WaitForAssertion(
            () => Assert.Contains("data-alert-rule-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Condition builder", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Speeding geofence", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rule-test", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-rule-enable", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCreate_WhenBindingMissing_RendersStateBeforeForm()
    {
        var page = RenderCreate(new FakeRulesDataSource
        {
            ListView = new OperateAlertRulesView([], MissingBinding)
        });

        page.WaitForAssertion(
            () => Assert.Contains("Alert-rule creation is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("honua-server#1169", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rule-create-submit", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCreate_WhenCreateIsForbidden_RendersForbiddenState()
    {
        var forbidden = new OperateAlertRulesBindingState(
            "Alert rules", OperateAlertRulesBindingState.Forbidden, "honua-server#1169", "Operator is not allowed.");
        var page = RenderCreate(new FakeRulesDataSource
        {
            CreateResult = OperateAlertRuleSaveResult.Blocked(forbidden)
        });

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-create-submit]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-create-submit]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Operator is not allowed.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("forbidden", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleDetail_WhenTestBindingFails_RendersBindingState()
    {
        var forbidden = new OperateAlertRulesBindingState(
            "Alert rules", OperateAlertRulesBindingState.Forbidden, "honua-server#1169", "Testing is forbidden.");
        var page = RenderDetail(
            new FakeRulesDataSource
            {
                DetailView = new OperateAlertRuleDetailView(SampleDefinition),
                TestResult = new OperateAlertRuleTestResult(false, [], [], forbidden)
            },
            "rule-1");

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-test]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-test]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Testing is forbidden.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Rule test is not bound", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft validation failed", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleDetail_WhenEnablingTestedDraft_PersistsDraftBeforeEnable()
    {
        OperateAlertRuleEdit? savedEdit = null;
        var page = RenderDetail(
            new FakeRulesDataSource
            {
                DetailView = new OperateAlertRuleDetailView(SampleDefinition with { Enabled = false }),
                TestResult = new OperateAlertRuleTestResult(true, [], []),
                SaveHandler = edit =>
                {
                    savedEdit = edit;
                    return new OperateAlertRuleSaveResult(SampleDefinition with { Enabled = false });
                },
                EnableHandler = (_, enabled) => new OperateAlertRuleSaveResult(SampleDefinition with { Enabled = enabled })
            },
            "rule-1");

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-test]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-test]").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("[data-rule-enable]").HasAttribute("disabled")), TimeSpan.FromSeconds(5));

        page.Find("[data-rule-enable]").Click();

        page.WaitForAssertion(() => Assert.NotNull(savedEdit), TimeSpan.FromSeconds(5));
        Assert.Equal("rule-1", savedEdit!.RuleId);
        Assert.False(savedEdit.Enabled);
        Assert.Contains("speed", savedEdit.Condition.Subject, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperateAlertRulesPage> RenderList(IOperateAlertRulesDataSource data)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRulesPage>();
    }

    private static IRenderedComponent<OperateAlertRuleDetailPage> RenderDetail(IOperateAlertRulesDataSource data, string ruleId)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRuleDetailPage>(parameters => parameters.Add(p => p.RuleId, ruleId));
    }

    private static IRenderedComponent<OperateAlertRuleCreatePage> RenderCreate(IOperateAlertRulesDataSource data)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRuleCreatePage>();
    }

    private static readonly OperateAlertRule SampleRule = new(
        "rule-1", "Speeding geofence", "geofence", Enabled: true,
        new OperateStatus("healthy", "Evaluated."), "speed > 60 in zone-9", "Slack #ops",
        "2026-06-01T00:00:00Z", ActiveIncidentCount: 2, DeliveryFailureCount: 0, ValidationMessages: []);

    private static readonly OperateAlertRuleDefinition SampleDefinition = new(
        "rule-1", "Speeding geofence", "geofence", Enabled: true,
        new OperateStatus("healthy", "Evaluated."), "Raise when a vehicle exceeds 60 in zone-9.",
        new OperateAlertRuleCondition("speed", "gte", "60", "5m", GeofenceZoneId: "zone-9", DwellMinutes: null),
        "Slack #ops", ["slack"], "2026-06-01T00:00:00Z",
        ActiveIncidentCount: 2, DeliveryFailureCount: 0, ValidationMessages: []);

    private sealed class FakeRulesDataSource : IOperateAlertRulesDataSource
    {
        public OperateAlertRulesView ListView { get; set; } = new([]);
        public OperateAlertRuleDetailView DetailView { get; set; } = new(Rule: null);
        public OperateAlertRuleSaveResult? SaveResult { get; set; }
        public OperateAlertRuleSaveResult? CreateResult { get; set; }
        public OperateAlertRuleTestResult? TestResult { get; set; }
        public Func<OperateAlertRuleEdit, OperateAlertRuleSaveResult>? SaveHandler { get; set; }
        public Func<string, bool, OperateAlertRuleSaveResult>? EnableHandler { get; set; }

        public Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ListView);

        public Task<OperateAlertRuleDetailView> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailView);

        public Task<OperateAlertRuleSaveResult> SaveRuleAsync(OperateAlertRuleEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveHandler?.Invoke(edit) ?? SaveResult ?? OperateAlertRuleSaveResult.Blocked(MissingBinding));

        public Task<OperateAlertRuleTestResult> TestRuleAsync(OperateAlertRuleDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateAlertRuleTestResult(true, [], []));

        public Task<OperateAlertRuleTestResult> TestRuleAsync(OperateAlertRuleEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromResult(TestResult ?? new OperateAlertRuleTestResult(true, [], []));

        public Task<OperateAlertRuleSaveResult> CreateRuleAsync(OperateAlertRuleDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateResult ?? SaveResult ?? OperateAlertRuleSaveResult.Blocked(MissingBinding));

        public Task<OperateAlertRuleSaveResult> SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default) =>
            Task.FromResult(EnableHandler?.Invoke(ruleId, enabled) ?? SaveResult ?? OperateAlertRuleSaveResult.Blocked(MissingBinding));
    }
}
