using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

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

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void RulesList_PresentationKeepsRowsAndCreateAction(ConsolePresentationMode presentation)
    {
        var page = RenderList(new FakeRulesDataSource { ListView = new OperateAlertRulesView([SampleRule]) }, presentation);

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tr[data-alert-rule='rule-1']")), TimeSpan.FromSeconds(5));
        AssertPresentation(page, presentation, ("Alert rules", "Alert rules"));
        Assert.Single(page.FindAll("[data-alert-rule-create]"));
        Assert.Equal("/operate/alerts/rules/rule-1", page.Find("tr[data-alert-rule='rule-1'] a").GetAttribute("href"));
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, page.FindAll("#alert-rules-heading").Count);
        if (presentation == ConsolePresentationMode.V4) CatalogAdminPreview.CaptureV4("alert-rules-list", page.Markup);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void RulesList_PresentationKeepsMissingBindingState(ConsolePresentationMode presentation)
    {
        var page = RenderList(new FakeRulesDataSource { ListView = new OperateAlertRulesView([], MissingBinding) }, presentation);

        page.WaitForAssertion(
            () => Assert.Contains("Alert rules are not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        AssertPresentation(page, presentation, ("Alert rules", "Alert rules"));
        Assert.Empty(page.FindAll("table"));
        if (presentation == ConsolePresentationMode.V4) CatalogAdminPreview.CaptureV4("alert-rules-unbound", page.Markup);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void RuleCreate_PresentationKeepsFormInsidePanel(ConsolePresentationMode presentation)
    {
        var page = RenderCreate(new FakeRulesDataSource(), presentation);

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-rule-create-submit]")), TimeSpan.FromSeconds(5));
        AssertPresentation(page, presentation, ("Create alert rule", "Create alert rule"));
        Assert.Equal(10, page.FindAll(".studio-field-grid label").Count);
        Assert.Equal("Back to alert rules", Assert.Single(page.FindAll("a[href='/operate/alerts/rules']")).TextContent);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.NotNull(page.Find("#alert-rule-create-heading").Closest(".console-panel")!.QuerySelector("[data-rule-create-submit]"));
        }

        if (presentation == ConsolePresentationMode.V4) CatalogAdminPreview.CaptureV4("alert-rules-create", page.Markup);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void RuleDetail_PresentationKeepsOverviewEditorAndGuardedEnable(ConsolePresentationMode presentation)
    {
        var page = RenderDetail(
            new FakeRulesDataSource { DetailView = new OperateAlertRuleDetailView(SampleDefinition with { Enabled = false }) },
            "rule-1",
            presentation);

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-alert-rule-editor]")), TimeSpan.FromSeconds(5));
        AssertPresentation(page, presentation, ("Speeding geofence", "Alert rule"));
        Assert.Contains("Condition builder", page.Markup, StringComparison.Ordinal);
        Assert.Contains("zone-9", page.Find("[data-rule-zone]").GetAttribute("value"), StringComparison.Ordinal);
        Assert.True(page.Find("[data-rule-enable]").HasAttribute("disabled"));
        Assert.Single(page.FindAll("[data-rule-save]"));
        Assert.Single(page.FindAll("[data-rule-test]"));
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 2 : 0,
            page.FindAll("#alert-rule-overview-heading, #alert-rule-condition-heading").Count);
        if (presentation == ConsolePresentationMode.V4) CatalogAdminPreview.CaptureV4("alert-rules-detail", page.Markup);
    }

    [Fact]
    public void RuleDetail_V4_TestedDraftEnablesThroughPanelHandlers()
    {
        var enabled = false;
        var page = RenderDetail(
            new FakeRulesDataSource
            {
                DetailView = new OperateAlertRuleDetailView(SampleDefinition with { Enabled = false }),
                SaveHandler = _ => new OperateAlertRuleSaveResult(SampleDefinition with { Enabled = false }),
                EnableHandler = (_, value) =>
                {
                    enabled = value;
                    return new OperateAlertRuleSaveResult(SampleDefinition with { Enabled = value });
                }
            },
            "rule-1",
            ConsolePresentationMode.V4);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-test]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-test]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-test-result]")), TimeSpan.FromSeconds(5));
        Assert.False(page.Find("[data-rule-enable]").HasAttribute("disabled"));

        page.Find("[data-rule-enable]").Click();

        page.WaitForAssertion(() => Assert.True(enabled), TimeSpan.FromSeconds(5));
        page.WaitForAssertion(() => Assert.Equal("Disable rule", page.Find("[data-rule-enable]").TextContent.Trim()), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RuleCreate_V4_SubmitRendersRefusalInsidePanel()
    {
        var forbidden = new OperateAlertRulesBindingState(
            "Alert rules", OperateAlertRulesBindingState.Forbidden, "honua-server#1169", "Operator is not allowed.");
        var page = RenderCreate(
            new FakeRulesDataSource { CreateResult = OperateAlertRuleSaveResult.Blocked(forbidden) },
            ConsolePresentationMode.V4);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-create-submit]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-create-submit]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Operator is not allowed.", page.Find("#alert-rule-create-heading").Closest(".console-panel")!.TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal("Rule was not created", page.Find(".console-panel h3").TextContent);
    }

    private static Bunit.BunitContext CreateContext(ConsolePresentationMode presentation)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        return ctx;
    }

    private static void AssertPresentation<TPage>(
        IRenderedComponent<TPage> page,
        ConsolePresentationMode presentation,
        (string V4, string Legacy) heading)
        where TPage : Microsoft.AspNetCore.Components.IComponent
    {
        var v4 = presentation == ConsolePresentationMode.V4;
        Assert.Equal(v4 ? heading.V4 : heading.Legacy, Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(v4, page.Markup.Contains("console-page-heading-v4", StringComparison.Ordinal));
        Assert.Equal(v4, page.Markup.Contains("console-alert-rules-v4", StringComparison.Ordinal));
        Assert.Equal(v4, page.Markup.Contains("operate-alert-rules-v4.css", StringComparison.Ordinal));
    }

    private static IRenderedComponent<OperateAlertRulesPage> RenderList(IOperateAlertRulesDataSource data, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRulesPage>();
    }

    private static IRenderedComponent<OperateAlertRuleDetailPage> RenderDetail(IOperateAlertRulesDataSource data, string ruleId, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRuleDetailPage>(parameters => parameters.Add(p => p.RuleId, ruleId));
    }

    private static IRenderedComponent<OperateAlertRuleCreatePage> RenderCreate(IOperateAlertRulesDataSource data, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
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
