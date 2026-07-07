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
/// Coverage for the console home's approval-inbox band going live (console#292 scope item 4):
/// it now subscribes to the same admin-hub proposals group the approval inbox uses, instead of
/// the one-shot read it had on trunk, and renders an honest Live/Manual pill rather than
/// claiming a liveness the connection does not have.
/// </summary>
public sealed class ConsoleHomePageTests
{
    [Fact]
    public async Task RealtimeConnected_ShowsLivePillAndStartsTheSubscription()
    {
        var realtime = new StubRealtimeClient { Connected = true };
        var html = await RenderAsync(
            inbox: new StubInboxClient
            {
                Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty),
            },
            realtime: realtime);

        Assert.True(realtime.StartCalled);
        Assert.Contains("data-home-live-state", html, StringComparison.Ordinal);
        Assert.Contains("is-live", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealtimeNotConnected_ShowsManualNotAFakeLivePill()
    {
        var html = await RenderAsync(
            inbox: new StubInboxClient
            {
                Result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty),
            },
            realtime: new StubRealtimeClient { Connected = false });

        Assert.Contains("Manual", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(StubInboxClient inbox, StubRealtimeClient realtime)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IConsoleEnvironmentProfileStore>(new StubProfileStore());
        services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);
        services.AddSingleton<IConsoleHostCapabilities>(new StubHostCapabilities());

        // ConsoleHomePage now embeds the shared ops-summary strip (console#292 scope item 2);
        // it needs its own narrow dependencies satisfied. Denied reads are enough for this test.
        services.AddSingleton<IOpsHealthDataSource>(new StubOpsHealthDataSource());
        services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient());

        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ConsoleHomePage>(ParameterView.Empty);
            return output.ToHtmlString();
        });
    }

    private sealed class StubProfileStore : IConsoleEnvironmentProfileStore
    {
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConsoleEnvironmentProfile>>([]);

        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentProfile?>(null);

        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentProfile?>(null);

        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ConsoleEnvironmentState?>(null);

        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class StubHostCapabilities : IConsoleHostCapabilities
    {
        public string HostKind => "browser";

        public bool SupportsNativeTransports => false;
    }

    private sealed class StubInboxClient : IConsoleApprovalInboxClient
    {
        public OperateSectionResult<ApprovalInboxSnapshot> Result { get; set; } =
            OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class StubOpsHealthDataSource : IOpsHealthDataSource
    {
        public Task<OperateSectionResult<OpsHealthView>> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OpsHealthView>.Denied(OperateSectionStatus.Unavailable, "n/a"));
    }

    private sealed class StubOpsFindingsClient : IConsoleOpsFindingsClient
    {
        public Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "n/a"));

        public Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(string findingId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class StubRealtimeClient : IConsoleProposalRealtimeClient
    {
        public bool Connected { get; set; }

        public bool StartCalled { get; private set; }

        public bool IsConnected => Connected;

        public event Action<ConsoleProposalEvent>? ProposalChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
