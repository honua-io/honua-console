using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateAlertRuleNavigationTests
{
    public enum PendingOperation { Save, Test, Disable }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void SameRuleParametersPreserveDraftAndTestQualification(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedRules();
        context.Services.AddSingleton<IOperateAlertRulesDataSource>(source);
        var page = Render(context);
        page.Find("[data-rule-name]").Change("draft name");
        page.Find("[data-rule-threshold]").Change("75");
        page.Find("[data-rule-test]").Click();

        page.Render(p => p.Add(c => c.RuleId, "first"));

        Assert.Equal("draft name", page.Find("[data-rule-name]").GetAttribute("value"));
        Assert.Equal("75", page.Find("[data-rule-threshold]").GetAttribute("value"));
        Assert.Single(page.FindAll("[data-rule-test-result]"));
        Assert.False(page.Find("[data-rule-enable]").HasAttribute("disabled"));
        Assert.Equal(["first"], source.Reads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task PreviousReadCannotReplaceCurrentRule(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedRules { DelayRead = true };
        context.Services.AddSingleton<IOperateAlertRulesDataSource>(source);
        var page = Render(context);
        page.Render(p => p.Add(c => c.RuleId, "second"));
        page.Find("[data-rule-name]").Change("second draft");
        var renders = page.RenderCount;

        await page.InvokeAsync(() => source.Read.SetResult(new(Definition("first"))));
        page.WaitForState(() => page.RenderCount > renders);

        Assert.Equal("second draft", page.Find("[data-rule-name]").GetAttribute("value"));
        Assert.Contains("second", page.Markup);
        Assert.Empty(page.FindAll("[data-rule-saved]"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, PendingOperation.Save)]
    [InlineData(ConsolePresentationMode.V4, PendingOperation.Save)]
    [InlineData(ConsolePresentationMode.Legacy, PendingOperation.Test)]
    [InlineData(ConsolePresentationMode.V4, PendingOperation.Test)]
    [InlineData(ConsolePresentationMode.Legacy, PendingOperation.Disable)]
    [InlineData(ConsolePresentationMode.V4, PendingOperation.Disable)]
    public async Task PreviousOperationCannotReplaceCurrentRuleOrBusyState(ConsolePresentationMode presentation, PendingOperation operation)
    {
        using var context = Context(presentation);
        var source = new DelayedRules { Pending = operation };
        context.Services.AddSingleton<IOperateAlertRulesDataSource>(source);
        var page = Render(context);
        page.Find("[data-rule-name]").Change("submitted first");
        var selector = operation switch
        {
            PendingOperation.Save => "[data-rule-save]",
            PendingOperation.Test => "[data-rule-test]",
            _ => "[data-rule-enable]"
        };
        var first = page.Find(selector).ClickAsync(new());
        page.Render(p => p.Add(c => c.RuleId, "second"));
        Assert.False(page.Find(selector).HasAttribute("disabled"));
        page.Find("[data-rule-name]").Change("second draft");
        var second = page.Find(selector).ClickAsync(new());

        await page.InvokeAsync(() => source.Complete("first"));
        await first;

        Assert.Equal("second draft", page.Find("[data-rule-name]").GetAttribute("value"));
        Assert.True(page.Find(selector).HasAttribute("disabled"));
        Assert.Empty(page.FindAll("[data-rule-saved]"));
        Assert.Empty(page.FindAll("[data-rule-test-result]"));
        await page.InvokeAsync(() => source.Complete("second"));
        await second;
        Assert.Equal(operation == PendingOperation.Disable, page.Find(selector).HasAttribute("disabled"));
        Assert.Single(page.FindAll(operation == PendingOperation.Test ? "[data-rule-test-result]" : "[data-rule-saved]"));
        Assert.Equal(["first", "second"], source.Targets);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EnablePrerequisiteCannotRetargetAfterNavigation(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedRules { Pending = PendingOperation.Save };
        context.Services.AddSingleton<IOperateAlertRulesDataSource>(source);
        var page = Render(context);
        page.Find("[data-rule-test]").Click();
        var enable = page.Find("[data-rule-enable]").ClickAsync(new());
        page.Render(p => p.Add(c => c.RuleId, "second"));
        page.Find("[data-rule-name]").Change("second draft");

        await page.InvokeAsync(() => source.Complete("first"));
        await enable;

        Assert.Equal("second draft", page.Find("[data-rule-name]").GetAttribute("value"));
        Assert.Empty(source.EnabledTargets);
        Assert.Empty(page.FindAll("[data-rule-saved]"));
        Assert.True(page.Find("[data-rule-enable]").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ReturningToRuleRejectsEarlierSaveAndPreservesNewEdits(ConsolePresentationMode presentation)
    {
        using var context = Context(presentation);
        var source = new DelayedRules { Pending = PendingOperation.Save };
        context.Services.AddSingleton<IOperateAlertRulesDataSource>(source);
        var page = Render(context);
        page.Find("[data-rule-name]").Change("old draft");
        var save = page.Find("[data-rule-save]").ClickAsync(new());
        page.Render(p => p.Add(c => c.RuleId, "second"));
        page.Render(p => p.Add(c => c.RuleId, "first"));
        page.Find("[data-rule-name]").Change("new visit draft");

        await page.InvokeAsync(() => source.Complete("first"));
        await save;

        Assert.Equal("new visit draft", page.Find("[data-rule-name]").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-rule-saved]"));
        Assert.Equal(["first", "second", "first"], source.Reads);
    }

    private static BunitContext Context(ConsolePresentationMode presentation)
    {
        var context = new BunitContext();
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }

    private static IRenderedComponent<OperateAlertRuleDetailPage> Render(BunitContext context) =>
        context.Render<OperateAlertRuleDetailPage>(p => p.Add(c => c.RuleId, "first"));

    private static OperateAlertRuleDefinition Definition(string id) =>
        OperateAlertRulesTestFixture.SampleDefinition with { RuleId = id, Name = id, Enabled = false };

    private sealed class DelayedRules : IOperateAlertRulesDataSource
    {
        public bool DelayRead { get; init; }
        public PendingOperation? Pending { get; init; }
        public List<string> Reads { get; } = [];
        public List<string> Targets { get; } = [];
        public List<string> EnabledTargets { get; } = [];
        public TaskCompletionSource<OperateAlertRuleDetailView> Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Dictionary<string, TaskCompletionSource<OperateAlertRuleSaveResult>> _saves = [];
        private readonly Dictionary<string, TaskCompletionSource<OperateAlertRuleTestResult>> _tests = [];

        public Task<OperateAlertRuleDetailView> GetRuleAsync(string ruleId, CancellationToken cancellationToken = default)
        {
            Reads.Add(ruleId);
            return ruleId == "first" && DelayRead ? Read.Task : Task.FromResult(new OperateAlertRuleDetailView(
                Definition(ruleId) with { Enabled = Pending == PendingOperation.Disable }));
        }

        public Task<OperateAlertRuleSaveResult> SaveRuleAsync(OperateAlertRuleEdit edit, CancellationToken cancellationToken = default)
        {
            Targets.Add(edit.RuleId);
            var pending = new TaskCompletionSource<OperateAlertRuleSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _saves.Add(edit.RuleId, pending);
            return pending.Task;
        }

        public Task<OperateAlertRuleTestResult> TestRuleAsync(OperateAlertRuleEdit edit, CancellationToken cancellationToken = default)
        {
            if (Pending != PendingOperation.Test) return Task.FromResult(new OperateAlertRuleTestResult(true, [], []));
            Targets.Add(edit.RuleId);
            var pending = new TaskCompletionSource<OperateAlertRuleTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tests.Add(edit.RuleId, pending);
            return pending.Task;
        }

        public Task<OperateAlertRuleSaveResult> SetRuleEnabledAsync(string ruleId, bool enabled, CancellationToken cancellationToken = default)
        {
            EnabledTargets.Add(ruleId);
            if (Pending != PendingOperation.Disable) return Task.FromResult(new OperateAlertRuleSaveResult(Definition(ruleId) with { Enabled = enabled }));
            Targets.Add(ruleId);
            var pending = new TaskCompletionSource<OperateAlertRuleSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _saves.Add(ruleId, pending);
            return pending.Task;
        }

        public void Complete(string id)
        {
            if (Pending == PendingOperation.Test) _tests[id].SetResult(new(true, [], []));
            else _saves[id].SetResult(new(Definition(id)));
        }

        public Task<OperateAlertRulesView> GetRulesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateAlertRuleTestResult> TestRuleAsync(OperateAlertRuleDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateAlertRuleSaveResult> CreateRuleAsync(OperateAlertRuleDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
