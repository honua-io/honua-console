using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the temporal viewer capability gate (<c>/operate/temporal</c>,
/// honua-console#43). Verifies that an unbound source surfaces the missing-binding capability
/// explanation rather than an empty viewer (issue AC #1), and that the registered merged-build client
/// (<see cref="UnsupportedTemporalCapabilityClient"/>) reports the missing binding for honua-server
/// #1166/#1167 without fabricating temporal data. Drives the page through a fake
/// <see cref="ITemporalCapabilityClient"/> rather than a mock server.
/// </summary>
public sealed class OperateTemporalPageRenderTests
{
    [Fact]
    public void TemporalViewer_WhenBindingMissing_RendersCapabilityExplanationNotEmptyViewer()
    {
        var data = new FakeTemporalClient
        {
            Workspace = new TemporalViewerWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ITemporalCapabilityClient>(data);

        var page = ctx.RenderComponent<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporal capability is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // AC#1: an explanation, not an empty source table.
        Assert.Contains("honua-server#1166", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemporalViewer_MergedBuildClient_ReportsMissingBindingWithoutMockData()
    {
        var client = new UnsupportedTemporalCapabilityClient();

        var workspace = await client.GetWorkspaceAsync();

        Assert.Empty(workspace.Sources);
        var state = Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing binding", state.State);
        Assert.Contains("honua-server#1166", state.Contract, StringComparison.Ordinal);
        Assert.Contains("honua-server#1167", state.Contract, StringComparison.Ordinal);
    }

    [Fact]
    public void TemporalViewer_WhenSourcesBound_RendersServerDeclaredCapabilityModes()
    {
        var data = new FakeTemporalClient
        {
            Workspace = new TemporalViewerWorkspace(
                [
                    new TemporalSourceCapability(
                        "src-parcels",
                        "parcels",
                        "0",
                        TemporalMode.Rollback,
                        TemporalSyncCapability.Bidirectional,
                        RollbackSupported: true,
                        SyncConflictReviewSupported: true,
                        RetentionPolicyId: "retain-7y"),
                ],
                [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<ITemporalCapabilityClient>(data);

        var page = ctx.RenderComponent<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains("parcels", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Rollback", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Bidirectional", page.Markup, StringComparison.Ordinal);
        Assert.Contains("retain-7y", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporal capability is not bound", page.Markup, StringComparison.Ordinal);
    }

    private static readonly TemporalCapabilityState MissingBinding = new(
        "Temporal viewer",
        "Missing binding",
        "honua-server#1166 / honua-server#1167",
        "Temporal history and disconnected sync conflict review bind to honua-server#1166/#1167.");

    private sealed class FakeTemporalClient : ITemporalCapabilityClient
    {
        public TemporalViewerWorkspace Workspace { get; set; } = new([], []);

        public Task<TemporalViewerWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);
    }
}
