using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using static Honua.Console.IntegrationTests.OperateAlertRulesTestFixture;

namespace Honua.Console.IntegrationTests;

public sealed class OperateAlertRulesPresentationTests
{
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
    public void RuleDetail_V4_SaveSendsBoundFieldValues()
    {
        OperateAlertRuleEdit? savedEdit = null;
        var page = RenderDetail(
            new FakeRulesDataSource
            {
                DetailView = new OperateAlertRuleDetailView(SampleDefinition),
                SaveHandler = edit =>
                {
                    savedEdit = edit;
                    return new OperateAlertRuleSaveResult(SampleDefinition);
                }
            },
            "rule-1",
            ConsolePresentationMode.V4);

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-zone]")), TimeSpan.FromSeconds(5));
        page.Find("[data-rule-zone]").Change("zone-12");
        page.Find("[data-rule-save]").Click();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-rule-saved]")), TimeSpan.FromSeconds(5));
        Assert.Equal("zone-12", savedEdit!.Condition.GeofenceZoneId);
    }

    [Fact]
    public void RulesList_WithoutPresentationOptions_RendersLegacy()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IOperateAlertRulesDataSource>(new FakeRulesDataSource { ListView = new OperateAlertRulesView([SampleRule]) });
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateAlertRulesPage>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("tr[data-alert-rule='rule-1']")), TimeSpan.FromSeconds(5));
        AssertPresentation(page, ConsolePresentationMode.Legacy, ("Alert rules", "Alert rules"));
        Assert.Equal("Actions", page.Find("th .console-visually-hidden").TextContent);
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
        var panel = page.Find("#alert-rule-create-heading").Closest(".console-panel")!;
        Assert.Equal("Rule was not created", panel.QuerySelector("h3")!.TextContent.Trim());
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
}
