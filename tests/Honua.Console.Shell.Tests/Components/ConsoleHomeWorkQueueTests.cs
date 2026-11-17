using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests.Components;

public sealed class ConsoleHomeWorkQueueTests : ConsoleComponentTestBase
{
    private readonly ManualTimeProvider _clock = new(TimeSpan.FromSeconds(10));
    private readonly InboxClient _inbox = new();
    private readonly RealtimeClient _realtime = new();

    public enum ReadFailure { Partial, Unavailable }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, ReadFailure.Partial)]
    [InlineData(ConsolePresentationMode.V4, ReadFailure.Partial)]
    [InlineData(ConsolePresentationMode.Legacy, ReadFailure.Unavailable)]
    [InlineData(ConsolePresentationMode.V4, ReadFailure.Unavailable)]
    public async Task FailedRead_HidesCountsAndRecoversWithoutNavigation(ConsolePresentationMode mode, ReadFailure failure)
    {
        _inbox.Result = Failed(failure);
        var component = Mount(mode);
        Assert.Single(component.FindAll("[data-home-inbox-error]"));
        Assert.Empty(component.FindAll("[data-home-awaiting-count], [data-home-total-count]"));
        Assert.Contains("Never loaded", component.Markup);

        _inbox.Result = Complete(3);
        await component.InvokeAsync(_clock.Tick);
        component.WaitForAssertion(() => Assert.Contains("3", component.Find("[data-home-awaiting-count]").TextContent));
        Assert.Empty(component.FindAll("[data-home-inbox-error]"));
        var reads = _inbox.Reads;
        await component.InvokeAsync(_clock.Tick);
        Assert.Equal(reads, _inbox.Reads);
    }

    [Fact]
    public async Task Reconnection_RefreshesCompleteButStaleCount()
    {
        var component = Mount();
        _inbox.Result = Complete(4);
        await component.InvokeAsync(() => _realtime.Connect());
        component.WaitForAssertion(() => Assert.Contains("4", component.Find("[data-home-awaiting-count]").TextContent));
    }

    [Theory]
    [InlineData(ReadFailure.Partial)]
    [InlineData(ReadFailure.Unavailable)]
    public async Task ReplayReset_FailedReadRendersErrorBeforeRejectingAcknowledgement(ReadFailure failure)
    {
        var component = Mount();
        _inbox.Result = Failed(failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _realtime.ResetAsync());
        Assert.Single(component.FindAll("[data-home-inbox-error]"));
        Assert.Empty(component.FindAll("[data-home-awaiting-count]"));
        _inbox.Result = Complete(5);
        await _realtime.ResetAsync();
        component.WaitForAssertion(() => Assert.Contains("5", component.Find("[data-home-awaiting-count]").TextContent));
    }

    [Fact]
    public async Task PendingRefresh_HidesOldCountAndDisposalRejectsLateResult()
    {
        var component = Mount();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inbox.Pending = pending.Task;
        var reset = _realtime.ResetAsync();
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-home-awaiting-count]"));
            Assert.Contains("Reading the work queue", component.Markup);
        });
        await component.Instance.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reset);
        Assert.True(_inbox.LastToken.IsCancellationRequested);
        var reads = _inbox.Reads;
        pending.SetResult(Complete(99));
        _clock.Tick();
        _realtime.Connect();
        Assert.Equal(reads, _inbox.Reads);
        Assert.False(_realtime.HasObservers);
    }

    [Fact]
    public async Task ForbiddenRead_DoesNotRetryPrivilegedInbox()
    {
        _inbox.Result = OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Forbidden, "Access denied.");
        var component = Mount();
        var reads = _inbox.Reads;
        await component.InvokeAsync(_clock.Tick);
        Assert.Equal(reads, _inbox.Reads);
        Assert.Empty(component.FindAll("[data-home-awaiting-count]"));
    }

    private IRenderedComponent<ConsoleHomeWorkQueue> Mount(ConsolePresentationMode mode = ConsolePresentationMode.Legacy)
    {
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton<IConsoleApprovalInboxClient>(_inbox);
        Services.AddSingleton<IConsoleProposalRealtimeClient>(_realtime);
        Services.AddSingleton(new ConsoleV4Options { Enabled = mode == ConsolePresentationMode.V4 });
        return Render<ConsoleHomeWorkQueue>();
    }

    private static OperateSectionResult<ApprovalInboxSnapshot> Failed(ReadFailure failure) => failure switch
    {
        ReadFailure.Partial => OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty,
            partialResult: true, message: "Some sources could not be read."),
        _ => OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "Server unreachable.")
    };

    private static OperateSectionResult<ApprovalInboxSnapshot> Complete(int count) =>
        OperateSectionResult<ApprovalInboxSnapshot>.Allowed(new ApprovalInboxSnapshot(
            Enumerable.Range(0, count).Select(index => new ApprovalInboxItem(ApprovalTicketType.Other,
                new ConsoleProposalSummary(index.ToString(), ConsoleProposalKind.Map, ConsoleProposalStatus.AwaitingApproval,
                    null, null, "Awaiting decision", ConsoleProposalRisk.Low, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).ToArray()));

    private sealed class InboxClient : IConsoleApprovalInboxClient
    {
        public OperateSectionResult<ApprovalInboxSnapshot> Result { get; set; } = Complete(1);
        public Task<OperateSectionResult<ApprovalInboxSnapshot>>? Pending { get; set; }
        public int Reads { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default)
        {
            Reads++;
            LastToken = cancellationToken;
            return Pending ?? Task.FromResult(Result);
        }
    }

    private sealed class RealtimeClient : IConsoleProposalRealtimeClient, IConsoleRealtimeCapabilityClient, IConsoleRealtimeRefreshClient
    {
        public event Action<ConsoleProposalEvent>? ProposalChanged;
        public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged;
        public event Func<Task>? RefreshRequired;
        public ConsoleRealtimeConnectionState ConnectionState { get; private set; } = ConsoleRealtimeConnectionState.Connecting;
        public bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;
        public bool HasObservers => ProposalChanged is not null || ConnectionStateChanged is not null || RefreshRequired is not null;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task ResetAsync() => RefreshRequired?.Invoke() ?? Task.CompletedTask;
        public void Connect()
        {
            ConnectionState = ConsoleRealtimeConnectionState.Connected;
            ConnectionStateChanged?.Invoke(ConnectionState);
        }
    }
}
