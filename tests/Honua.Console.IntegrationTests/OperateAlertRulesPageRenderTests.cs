using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using static Honua.Console.IntegrationTests.OperateAlertRulesTestFixture;

namespace Honua.Console.IntegrationTests;

public sealed class OperateAlertRulesPageRenderTests
{
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
}
