using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the Operate hub's "Current attention" list (console#292 scope item 5): a
/// connection diagnostic with an empty <c>OperatorActions</c> list used to throw
/// <see cref="IndexOutOfRangeException"/> from the unguarded <c>OperatorActions[0]</c> index at
/// <c>OperatePage.razor:181</c>. The guard must render an honest fallback instead of indexing an
/// empty list, and must not regress the normal (non-empty) path.
/// </summary>
public sealed class OperatePageTests
{
    [Fact]
    public async Task EmptyOperatorActions_RendersFallbackInsteadOfThrowing()
    {
        var workspace = new OperateTransitionWorkspace(
            Connections:
            [
                new OperateConnectionSummary(
                    "conn-1", "County SFTP", "sftp", "sftp://county.example", "svc-account", "degraded", "2026-07-07 09:00 HST",
                    new OperateConnectionDiagnostic(
                        Outcome: "failed",
                        FailureCode: "auth_failed",
                        Summary: "Authentication failed.",
                        Signals: [],
                        OperatorActions: [], // The empty-list case that used to throw.
                        Evidence: new Dictionary<string, string>())),
            ],
            ResourceEdits: [],
            Services: [],
            SettingsChanges: [],
            CapabilityStates: []);

        var html = await RenderAsync(workspace);

        Assert.Contains("No operator action recorded.", html);
        Assert.Contains("County SFTP", html);
        Assert.Contains("auth_failed", html);
    }

    [Fact]
    public async Task NonEmptyOperatorActions_StillRendersTheFirstAction()
    {
        var workspace = new OperateTransitionWorkspace(
            Connections:
            [
                new OperateConnectionSummary(
                    "conn-2", "County SFTP", "sftp", "sftp://county.example", "svc-account", "degraded", "2026-07-07 09:00 HST",
                    new OperateConnectionDiagnostic(
                        Outcome: "failed",
                        FailureCode: "auth_failed",
                        Summary: "Authentication failed.",
                        Signals: [],
                        OperatorActions: ["Rotate the service-account credential."],
                        Evidence: new Dictionary<string, string>())),
            ],
            ResourceEdits: [],
            Services: [],
            SettingsChanges: [],
            CapabilityStates: []);

        var html = await RenderAsync(workspace);

        Assert.Contains("Rotate the service-account credential.", html);
        Assert.DoesNotContain("No operator action recorded.", html);
    }

    private static async Task<string> RenderAsync(OperateTransitionWorkspace workspace)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IOperateTransitionDataSource>(new StubOperateTransitionDataSource(workspace));
        services.AddSingleton<IConsoleCapabilityManifest>(new ConsoleCapabilityManifest());

        // OperatePage now embeds the shared ops-summary strip (console#292 scope item 2); it
        // needs its own narrow dependencies satisfied. Denied reads are enough — this test is
        // only exercising the OperatorActions guard, not the strip's own rendering.
        services.AddSingleton<IOpsHealthDataSource>(new StubOpsHealthDataSource());
        services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient());
        services.AddSingleton<IConsoleApprovalInboxClient>(new StubApprovalInboxClient());
        services.AddSingleton<IConsoleProposalRealtimeClient>(new StubProposalRealtimeClient());

        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperatePage>(ParameterView.Empty);
            return output.ToHtmlString();
        });
    }

    private sealed class StubOperateTransitionDataSource(OperateTransitionWorkspace workspace) : IOperateTransitionDataSource
    {
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(workspace);

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(null);
    }

    private sealed class StubOpsHealthDataSource : IOpsHealthDataSource
    {
        public Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OpsHealthTrendView>> GetHistoryAsync(
            OpsHealthTrendRangeSelection selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OpsHealthTrendView>.Denied(OperateSectionStatus.Unsupported, "n/a"));
    }

    private sealed class StubOpsFindingsClient : IConsoleOpsFindingsClient
    {
        public Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(string findingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class StubApprovalInboxClient : IConsoleApprovalInboxClient
    {
        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "n/a"));
    }

    private sealed class StubProposalRealtimeClient : IConsoleProposalRealtimeClient
    {
        public bool IsConnected => false;

        public event Action<ConsoleProposalEvent>? ProposalChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
