using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests.Components;

public sealed partial class ConsoleApprovalCountTests
{
    [Fact]
    public Task PartialRead_RecoversWithoutNavigationOrProposalEvent() =>
        AssertIncompleteReadRecoversAsync(Count(0) with { PartialResult = true });

    [Fact]
    public Task UnavailableRead_RecoversWithoutNavigationOrProposalEvent() =>
        AssertIncompleteReadRecoversAsync(
            OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "Offline"));

    private async Task AssertIncompleteReadRecoversAsync(OperateSectionResult<ApprovalInboxSnapshot> incomplete)
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(incomplete);
        var component = Render<ConsoleApprovalCount>();
        Assert.Empty(component.FindAll("[data-count]"));
        inbox.Read = _ => Task.FromResult(Count(12));

        await component.InvokeAsync(clock.Tick);

        component.WaitForAssertion(() => Assert.Equal("12", component.Find("[data-count]").TextContent));
        Assert.Equal(2, inbox.Reads);
        await component.InvokeAsync(clock.Tick);
        Assert.Equal(2, inbox.Reads); // Complete projections do not poll the inbox.
    }

    [Fact]
    public async Task RepeatedFailures_RetryUntilTheCompleteReadArrives()
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(Count(0) with { PartialResult = true });
        var component = Render<ConsoleApprovalCount>();
        await component.InvokeAsync(clock.Tick);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));
        Assert.Empty(component.FindAll("[data-count]"));
        inbox.Read = _ => Task.FromResult(Count(3));

        await component.InvokeAsync(clock.Tick);

        component.WaitForAssertion(() => Assert.Equal("3", component.Find("[data-count]").TextContent));
    }

    [Fact]
    public async Task Disposal_StopsScheduledRecoveryReads()
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(Count(0) with { PartialResult = true });
        Render<ConsoleApprovalCount>();

        await DisposeAsync();
        clock.Tick();

        Assert.Equal(1, inbox.Reads);
        Assert.True(clock.Disposed);
    }

    [Fact]
    public async Task Recovery_ReadAlreadyPendingIsNotDuplicated()
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(Count(0) with { PartialResult = true });
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;
        await component.InvokeAsync(clock.Tick);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));

        await component.InvokeAsync(clock.Tick);
        Assert.Equal(2, inbox.Reads);
        pending.SetResult(Count(4));
        component.WaitForAssertion(() => Assert.Equal("4", component.Find("[data-count]").TextContent));
    }

    [Theory]
    [InlineData(ConsoleRealtimeConnectionState.FallbackEngaged)]
    [InlineData(ConsoleRealtimeConnectionState.NotConfigured)]
    [InlineData(ConsoleRealtimeConnectionState.Connecting)]
    public async Task DisconnectedStream_RefreshesMissedProposalsUntilRealtimeReconnects(ConsoleRealtimeConnectionState state)
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        await component.InvokeAsync(() => realtime.ChangeConnection(state));
        inbox.Read = _ => Task.FromResult(Count(7));

        await component.InvokeAsync(clock.Tick);

        component.WaitForAssertion(() => Assert.Equal("7", component.Find("[data-count]").TextContent));
        inbox.Read = _ => Task.FromResult(Count(8));
        await component.InvokeAsync(() => realtime.ChangeConnection(ConsoleRealtimeConnectionState.Connected));
        component.WaitForAssertion(() => Assert.Equal("8", component.Find("[data-count]").TextContent));
        var reads = inbox.Reads;
        await component.InvokeAsync(clock.Tick);
        Assert.Equal(reads, inbox.Reads);
    }

    [Theory]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Unsupported)]
    [InlineData(OperateSectionStatus.Missing)]
    public async Task PermanentFailure_DoesNotRetry(OperateSectionStatus status)
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(OperateSectionResult<ApprovalInboxSnapshot>.Denied(status, "Unavailable"));
        var component = Render<ConsoleApprovalCount>();

        await component.InvokeAsync(clock.Tick);

        Assert.Empty(component.FindAll("[data-count]"));
        Assert.Equal(1, inbox.Reads);
    }

    [Fact]
    public async Task SignOut_DuringScheduledReadCannotRestorePreviousOperatorsCount()
    {
        var clock = new RecoveryClock();
        Services.AddSingleton<TimeProvider>(clock);
        var authentication = new Authentication(true);
        var (inbox, _) = Register(authentication);
        inbox.Read = _ => Task.FromResult(Count(0) with { PartialResult = true });
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;
        await component.InvokeAsync(clock.Tick);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));

        await component.InvokeAsync(() => authentication.Change(false));
        pending.SetResult(Count(9));
        await component.InvokeAsync(clock.Tick);

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-count]")));
        Assert.Equal(2, inbox.Reads);
    }

    private sealed class RecoveryClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public bool Disposed { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            _state = state;
            return new RecoveryTimer(this);
        }
        public void Tick() { if (!Disposed) _callback?.Invoke(_state); }
        private sealed class RecoveryTimer(RecoveryClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => owner.Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
