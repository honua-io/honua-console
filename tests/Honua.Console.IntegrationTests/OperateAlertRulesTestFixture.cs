using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal static class OperateAlertRulesTestFixture
{
    internal static readonly OperateAlertRulesBindingState MissingBinding = new(
        "Alert rules", OperateAlertRulesBindingState.MissingBinding, "honua-server#1169",
        "Alert rule definitions bind to honua-server#1169.");

    private static Bunit.BunitContext CreateContext(ConsolePresentationMode presentation)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        return ctx;
    }

    internal static IRenderedComponent<OperateAlertRulesPage> RenderList(IOperateAlertRulesDataSource data, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRulesPage>();
    }

    internal static IRenderedComponent<OperateAlertRuleDetailPage> RenderDetail(IOperateAlertRulesDataSource data, string ruleId, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRuleDetailPage>(parameters => parameters.Add(p => p.RuleId, ruleId));
    }

    internal static IRenderedComponent<OperateAlertRuleCreatePage> RenderCreate(IOperateAlertRulesDataSource data, ConsolePresentationMode presentation = ConsolePresentationMode.Legacy)
    {
        var ctx = CreateContext(presentation);
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return ctx.Render<OperateAlertRuleCreatePage>();
    }

    internal static readonly OperateAlertRule SampleRule = new(
        "rule-1", "Speeding geofence", "geofence", Enabled: true,
        new OperateStatus("healthy", "Evaluated."), "speed > 60 in zone-9", "Slack #ops",
        "2026-06-01T00:00:00Z", ActiveIncidentCount: 2, DeliveryFailureCount: 0, ValidationMessages: []);

    internal static readonly OperateAlertRuleDefinition SampleDefinition = new(
        "rule-1", "Speeding geofence", "geofence", Enabled: true,
        new OperateStatus("healthy", "Evaluated."), "Raise when a vehicle exceeds 60 in zone-9.",
        new OperateAlertRuleCondition("speed", "gte", "60", "5m", GeofenceZoneId: "zone-9", DwellMinutes: null),
        "Slack #ops", ["slack"], "2026-06-01T00:00:00Z",
        ActiveIncidentCount: 2, DeliveryFailureCount: 0, ValidationMessages: []);

    internal sealed class FakeRulesDataSource : IOperateAlertRulesDataSource
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
