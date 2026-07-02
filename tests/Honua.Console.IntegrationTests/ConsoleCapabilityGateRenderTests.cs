using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the first-release capability-manifest gate
/// (docs/roadmap/FIRST_RELEASE_STRATEGY_AND_CUT_LINE.md). Deferred "exotic depth" surfaces must render
/// the first-class "unsupported" state — never dead/live UI — when the connected deployment does not
/// advertise the capability, and light up unchanged when it does.
/// </summary>
public sealed class ConsoleCapabilityGateRenderTests
{
    [Fact]
    public void Gate_WhenAdvertised_RendersChildContent()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(
            new ConsoleCapabilityManifest([ConsoleCapabilityKeys.Temporal]));

        var gate = ctx.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(p => p.Capability, ConsoleCapabilityKeys.Temporal)
            .AddChildContent("<div data-live=\"1\">live surface</div>"));

        Assert.Contains("live surface", gate.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("console-state-unsupported", gate.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Gate_WhenNotAdvertised_RendersUnsupportedStateAndHidesChildContent()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        var gate = ctx.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(p => p.Capability, ConsoleCapabilityKeys.Temporal)
            .Add(p => p.Title, "Temporal is not available in this release")
            .AddChildContent("<div data-live=\"1\">live surface</div>"));

        Assert.Contains("console-state-unsupported", gate.Markup, StringComparison.Ordinal);
        Assert.Contains("Temporal is not available in this release", gate.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("live surface", gate.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalPage_WhenCapabilityNotAdvertised_RendersUnsupportedNotViewer()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        var page = ctx.Render<OperateTemporalPage>();

        Assert.Contains("Temporal is not available in this release", page.Markup, StringComparison.Ordinal);
        // The deferred viewer never renders behind the unsupported state.
        Assert.DoesNotContain("Temporal capability is not bound", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncPage_WhenCapabilityNotAdvertised_RendersUnsupportedNotQueue()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        var page = ctx.Render<OperateSyncPage>();

        Assert.Contains("Disconnected sync review is not available in this release", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Sync conflict review is not bound", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AlertRulesPage_WhenCapabilityNotAdvertised_RendersUnsupportedNotRuleList()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IOperateAlertRulesDataSource, UnsupportedOperateAlertRulesDataSource>();
        ctx.Services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        var page = ctx.Render<OperateAlertRulesPage>();

        Assert.Contains("Realtime / geofence alerting is not available in this release", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Alert rules are not bound", page.Markup, StringComparison.Ordinal);
    }
}
