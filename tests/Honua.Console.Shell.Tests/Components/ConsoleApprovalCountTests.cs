using System.Security.Claims;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests.Components;

public sealed partial class ConsoleApprovalCountTests : ConsoleComponentTestBase
{
    [Fact]
    public async Task ReplayReset_ReconcilesCountWithoutAProposalEvent()
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        component.WaitForAssertion(() => Assert.Equal("0", component.Find("[data-count]").TextContent));
        inbox.Read = _ => Task.FromResult(Count(1));

        await component.InvokeAsync(realtime.ResetAsync);

        component.WaitForAssertion(() => Assert.Equal("1", component.Find("[data-count]").TextContent));
        Assert.Equal(2, inbox.Reads);
    }

    [Fact]
    public async Task ReplayReset_IsAcknowledgedOnlyAfterTheRestReadCompletes()
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;

        var reset = component.InvokeAsync(realtime.ResetAsync);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));
        Assert.False(reset.IsCompleted);
        pending.SetResult(Count(1));
        await reset;

        Assert.Equal("1", component.Find("[data-count]").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayReset_IncompleteReadDoesNotAcknowledgeReconciliation(bool partial)
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        inbox.Read = _ => Task.FromResult(partial
            ? Count(1) with { PartialResult = true }
            : OperateSectionResult<ApprovalInboxSnapshot>.Denied(OperateSectionStatus.Unavailable, "Unavailable"));

        await Assert.ThrowsAsync<IOException>(() => component.InvokeAsync(realtime.ResetAsync));

        Assert.Empty(component.FindAll("[data-count]"));
        inbox.Read = _ => Task.FromResult(Count(0));
        await component.InvokeAsync(realtime.ResetAsync);
        Assert.Equal("0", component.Find("[data-count]").TextContent);
    }

    [Fact]
    public async Task ReplayReset_TimeoutCancelsReadWithoutAcknowledgement()
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken readToken = default;
        inbox.Read = token => { readToken = token; return pending.Task; };

        await Assert.ThrowsAsync<IOException>(() => component.InvokeAsync(realtime.ResetAsync));

        Assert.True(readToken.IsCancellationRequested);
        Assert.Empty(component.FindAll("[data-count]"));
        pending.SetResult(Count(1));
    }

    [Fact]
    public async Task OlderRead_CannotOverwriteTheReconciledCount()
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;
        var olderReset = component.InvokeAsync(realtime.ResetAsync);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));
        inbox.Read = _ => Task.FromResult(Count(1));

        await component.InvokeAsync(realtime.ResetAsync);
        Assert.Equal("1", component.Find("[data-count]").TextContent);
        pending.SetResult(Count(0));
        await Assert.ThrowsAsync<IOException>(() => olderReset);

        Assert.Equal("1", component.Find("[data-count]").TextContent);
    }

    [Fact]
    public async Task Disposal_UnsubscribesReplayResetAndLocationChanges()
    {
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        component.WaitForAssertion(() => Assert.Equal(1, inbox.Reads));
        var navigation = Services.GetRequiredService<NavigationManager>();
        await DisposeAsync();

        await realtime.ResetAsync();
        navigation.NavigateTo("/operate/approvals");

        Assert.Equal(1, inbox.Reads);
        Assert.Equal(0, realtime.RefreshSubscribers);
    }

    [Fact]
    public async Task ProfileChange_ClearsOldCountUntilTheNewEnvironmentResponds()
    {
        var profiles = new Profiles();
        Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var (inbox, _) = Register();
        inbox.Read = _ => Task.FromResult(Count(1));
        var component = Render<ConsoleApprovalCount>();
        component.WaitForAssertion(() => Assert.Equal("1", component.Find("[data-count]").TextContent));
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;

        await component.InvokeAsync(profiles.Change);

        Assert.Empty(component.FindAll("[data-count]"));
        pending.SetResult(Count(0));
        component.WaitForAssertion(() => Assert.Equal("0", component.Find("[data-count]").TextContent));
    }

    [Fact]
    public async Task ProfileChange_OldEnvironmentResponseCannotReplaceNewCount()
    {
        var profiles = new Profiles();
        Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var (inbox, realtime) = Register();
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;
        var olderReset = component.InvokeAsync(realtime.ResetAsync);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));
        inbox.Read = _ => Task.FromResult(Count(0));

        await component.InvokeAsync(profiles.Change);
        component.WaitForAssertion(() => Assert.Equal("0", component.Find("[data-count]").TextContent));
        pending.SetResult(Count(1));
        await Assert.ThrowsAsync<IOException>(() => olderReset);

        Assert.Equal("0", component.Find("[data-count]").TextContent);
    }

    [Fact]
    public void AnonymousShell_DoesNotReadThePrivilegedInbox()
    {
        var (inbox, _) = Register(new Authentication(false));
        var component = Render<ConsoleApprovalCount>();

        Assert.Empty(component.FindAll("[data-count]"));
        Assert.Equal(0, inbox.Reads);
    }

    [Fact]
    public async Task SignOut_ClearsTheCountAndRejectsInFlightResults()
    {
        var authentication = new Authentication(true);
        var (inbox, realtime) = Register(authentication);
        inbox.Read = _ => Task.FromResult(Count(1));
        var component = Render<ConsoleApprovalCount>();
        component.WaitForAssertion(() => Assert.Equal("1", component.Find("[data-count]").TextContent));
        var pending = new TaskCompletionSource<OperateSectionResult<ApprovalInboxSnapshot>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Read = _ => pending.Task;
        var reset = component.InvokeAsync(realtime.ResetAsync);
        component.WaitForAssertion(() => Assert.Equal(2, inbox.Reads));

        await component.InvokeAsync(() => authentication.Change(false));
        Assert.Empty(component.FindAll("[data-count]"));
        pending.SetResult(Count(1));
        await Assert.ThrowsAsync<IOException>(() => reset);

        Assert.Empty(component.FindAll("[data-count]"));
        Assert.Equal(2, inbox.Reads);
    }

    [Fact]
    public async Task SignIn_RefreshesWithoutRequiringNavigation()
    {
        var authentication = new Authentication(false);
        var (inbox, _) = Register(authentication);
        inbox.Read = _ => Task.FromResult(Count(1));
        var component = Render<ConsoleApprovalCount>();

        await component.InvokeAsync(() => authentication.Change(true));

        component.WaitForAssertion(() => Assert.Equal("1", component.Find("[data-count]").TextContent));
        Assert.Equal(1, inbox.Reads);
    }

    [Fact]
    public async Task AuthenticationNotification_UsesItsPendingStateBeforeReadingTheInbox()
    {
        var authentication = new Authentication(false);
        var (inbox, _) = Register(authentication);
        var component = Render<ConsoleApprovalCount>();
        var pending = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);

        await component.InvokeAsync(() => authentication.Change(pending.Task));
        Assert.Equal(0, inbox.Reads);
        pending.SetResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic")], "test"))));

        component.WaitForAssertion(() => Assert.Equal(1, inbox.Reads));
        Assert.Equal("0", component.Find("[data-count]").TextContent);
    }

    [Fact]
    public async Task SignIn_RebindsProfileChangesToTheCurrentOperator()
    {
        var authentication = new Authentication(false);
        var profiles = new Profiles();
        Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var (inbox, _) = Register(authentication);
        var component = Render<ConsoleApprovalCount>();
        profiles.Operator = "signed-in";

        await component.InvokeAsync(() => authentication.Change(true));
        component.WaitForAssertion(() => Assert.Equal("0", component.Find("[data-count]").TextContent));
        inbox.Read = _ => Task.FromResult(Count(1));
        await component.InvokeAsync(profiles.Change);

        component.WaitForAssertion(() => Assert.Equal("1", component.Find("[data-count]").TextContent));
        Assert.Equal(2, inbox.Reads);
        await DisposeAsync();
        Assert.Equal(0, profiles.Subscribers);
    }

    private (Inbox Inbox, Realtime Realtime) Register(Authentication? authentication = null)
    {
        Services.AddSingleton<AuthenticationStateProvider>(authentication ?? new Authentication(true));
        var inbox = new Inbox();
        var realtime = new Realtime();
        Services.AddSingleton<IConsoleApprovalInboxClient>(inbox);
        Services.AddSingleton<IConsoleProposalRealtimeClient>(realtime);
        return (inbox, realtime);
    }

    private static OperateSectionResult<ApprovalInboxSnapshot> Count(int count) =>
        OperateSectionResult<ApprovalInboxSnapshot>.Allowed(new ApprovalInboxSnapshot(
            Enumerable.Range(0, count).Select(index => new ApprovalInboxItem(ApprovalTicketType.PublishData,
                new ConsoleProposalSummary($"p-{index}", ConsoleProposalKind.MetadataRelease,
                    ConsoleProposalStatus.AwaitingApproval, "tester", null, "Synthetic proposal",
                    ConsoleProposalRisk.Low, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).ToArray()));

    private sealed class Authentication(bool signedIn) : AuthenticationStateProvider
    {
        private bool _signedIn = signedIn;
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => CreateState();
        private Task<AuthenticationState> CreateState() => Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(_signedIn
                ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic")], "test")
                : new ClaimsIdentity())));

        public void Change(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);

        public void Change(bool signedIn)
        {
            _signedIn = signedIn;
            NotifyAuthenticationStateChanged(CreateState());
        }
    }

    private sealed class Profiles : IConsoleEnvironmentProfileStore
    {
        private readonly Dictionary<Action, string> _subscriptions = new();
        public string Operator { get; set; } = "anonymous";
        public int Subscribers => _subscriptions.Count;
        public event Action? ActiveProfileChanged
        {
            add { if (value is not null) _subscriptions[value] = Operator; }
            remove { if (value is not null) _subscriptions.Remove(value); }
        }
        public void Change()
        {
            foreach (var handler in _subscriptions.Where(pair => pair.Value == Operator).Select(pair => pair.Key).ToArray())
                handler();
        }
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class Inbox : IConsoleApprovalInboxClient
    {
        public int Reads { get; private set; }
        public Func<CancellationToken, Task<OperateSectionResult<ApprovalInboxSnapshot>>> Read { get; set; } =
            _ => Task.FromResult(Count(0));

        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Read(cancellationToken);
        }
    }

    private sealed class Realtime : IConsoleProposalRealtimeClient, IConsoleRealtimeRefreshClient, IConsoleRealtimeCapabilityClient
    {
        public ConsoleRealtimeConnectionState ConnectionState { get; private set; } = ConsoleRealtimeConnectionState.Connected;
        public bool IsConnected => ConnectionState == ConsoleRealtimeConnectionState.Connected;
        public bool IsFallbackEngaged => ConnectionState == ConsoleRealtimeConnectionState.FallbackEngaged;
        public event Action<ConsoleRealtimeConnectionState>? ConnectionStateChanged;
        public void ChangeConnection(ConsoleRealtimeConnectionState state)
        {
            ConnectionState = state;
            ConnectionStateChanged?.Invoke(state);
        }
        public int RefreshSubscribers => RefreshRequired?.GetInvocationList().Length ?? 0;
        public event Action<ConsoleProposalEvent>? ProposalChanged { add { } remove { } }
        public event Func<Task>? RefreshRequired;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task ResetAsync()
        {
            if (RefreshRequired is not { } handlers) return;
            foreach (Func<Task> handler in handlers.GetInvocationList()) await handler();
        }
    }
}
