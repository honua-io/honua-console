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

        public Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ListView);

        public Task<OperateAlertRuleDetailView> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult(DetailView);

        public Task<OperateAlertRuleSaveResult> SaveRuleAsync(OperateAlertRuleEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromResult(SaveResult ?? OperateAlertRuleSaveResult.Blocked(MissingBinding));
    }
}
