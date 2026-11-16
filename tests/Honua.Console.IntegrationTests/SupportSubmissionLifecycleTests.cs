using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.IntegrationTests;

public sealed class SupportSubmissionLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSubmissionLocksFormAndFailureRestoresDraft(bool enabled)
    {
        using var context = Context(new ConsoleV4Options { Enabled = enabled });
        var client = context.Services.GetRequiredService<DeferredClient>();
        var page = context.Render<SupportPage>();
        Submit(page);
        Assert.All(page.FindAll("article input, article select, article textarea"),
            control => Assert.True(control.HasAttribute("disabled")));
        Assert.Single(client.Requests);
        await page.InvokeAsync(() => client.Pending.SetResult(
            SupportTicketResult.Denied(OperateSectionStatus.Unavailable, "Support is unavailable.")));
        page.WaitForAssertion(() => Assert.Contains("Support is unavailable.", page.Find("[role=alert]").TextContent));
        Assert.All(page.FindAll("article input, article select, article textarea"),
            control => Assert.False(control.HasAttribute("disabled")));
        Assert.Equal("diagnose", page.FindAll("input:not([type=checkbox])")[1].GetAttribute("value"));
        Assert.Equal("errors", page.Find("textarea").GetAttribute("value") ?? page.Find("textarea").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSuccessfulSubmissionCannotNavigateAfterDisposal(bool enabled)
    {
        using var context = Context(new ConsoleV4Options { Enabled = enabled });
        var client = context.Services.GetRequiredService<DeferredClient>();
        var page = context.Render<SupportPage>();
        Submit(page);
        await context.DisposeComponentsAsync();
        var nav = context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/settings");
        await context.Renderer.Dispatcher.InvokeAsync(() => client.Pending.SetResult(Success()));
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        Assert.EndsWith("/settings", nav.Uri);
        Assert.True(client.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposedContextCaptureCannotStartTicketCreation()
    {
        using var context = Context(new ConsoleV4Options());
        var profiles = context.Services.GetRequiredService<DeferredProfiles>();
        var client = context.Services.GetRequiredService<DeferredClient>();
        var page = context.Render<SupportPage>();
        profiles.DelayNextCapture = true;
        Submit(page);
        Assert.Empty(client.Requests);
        await context.DisposeComponentsAsync();
        await context.Renderer.Dispatcher.InvokeAsync(() => profiles.Pending.SetResult(null));
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        Assert.Empty(client.Requests);
        Assert.True(profiles.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposalHandlesCooperativeSubmissionCancellation()
    {
        using var context = Context(new ConsoleV4Options());
        var client = context.Services.GetRequiredService<DeferredClient>();
        var page = context.Render<SupportPage>();
        Submit(page);
        await context.DisposeComponentsAsync();
        await context.Renderer.Dispatcher.InvokeAsync(() => client.Pending.SetCanceled(client.Token));
        await context.Renderer.Dispatcher.InvokeAsync(() => Task.CompletedTask);
        Assert.True(client.Token.IsCancellationRequested);
        Assert.Single(client.Requests);
    }

    [Fact]
    public async Task SelfServiceActivityDuringSubmissionIsRetainedForRetry()
    {
        using var context = Context(new ConsoleV4Options());
        var profiles = context.Services.GetRequiredService<DeferredProfiles>();
        var client = context.Services.GetRequiredService<DeferredClient>();
        var page = context.Render<SupportPage>();
        profiles.DelayNextCapture = true;
        Submit(page);
        await page.InvokeAsync(() => page.FindComponent<Stub<SupportSelfServicePanel>>().Instance.Parameters
            .Get(p => p.OnContextChanged).InvokeAsync(new SupportSelfServiceContext
            {
                ViewedKb = [new SupportKbRecord { Id = "later-article", Title = "Later advice" }]
            }));
        profiles.DelayNextCapture = false;
        await page.InvokeAsync(() => profiles.Pending.SetResult(null));
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(client.Requests);
        Assert.DoesNotContain("Later advice", client.Requests[0].Symptoms);
        await page.InvokeAsync(() => client.Pending.SetResult(
            SupportTicketResult.Denied(OperateSectionStatus.Unavailable, "Support is unavailable.")));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[role=alert]")));
        page.Find("article button").Click();
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains("Later advice", client.Requests[1].Symptoms);
    }

    private static void Submit(IRenderedComponent<SupportPage> page)
    {
        page.FindAll("input:not([type=checkbox])")[1].Change("diagnose");
        page.Find("textarea").Change("errors");
        page.Find("article button").Click();
    }

    private static BunitContext Context(ConsoleV4Options options)
    {
        var context = new BunitContext();
        context.ComponentFactories.AddStub<SupportSelfServicePanel>();
        context.Services.AddSingleton(options);
        context.Services.AddSingleton<DeferredClient>();
        context.Services.AddSingleton<IConsoleSupportTicketClient>(p => p.GetRequiredService<DeferredClient>());
        var profiles = new DeferredProfiles();
        var sessions = new InMemoryConsoleAccountSessionStore();
        context.Services.AddSingleton(profiles);
        context.Services.AddSingleton(new ConsoleSupportContextBuilder(profiles, sessions,
            new HttpConsoleOperateObservabilityClient(new HttpClient(), profiles, sessions)));
        context.Services.AddSingleton<IConsoleSupportDeflectionLog>(
            new ConsoleSupportDeflectionLog(NullLogger<ConsoleSupportDeflectionLog>.Instance));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/support");
        return context;
    }

    private static SupportTicketResult Success() => SupportTicketResult.Allowed(new SupportTicketResponse
    {
        Id = "created-ticket", CustomerId = "test", Severity = "sev3", Environment = "production",
        Service = "honua-server", Symptoms = "errors", RequestedAction = "diagnose",
        AllowedAccessMode = "read-only", Phase = "new", CustomerStatus = "open"
    });

    private sealed class DeferredClient : IConsoleSupportTicketClient
    {
        public TaskCompletionSource<SupportTicketResult> Pending { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<CreateSupportTicketRequest> Requests { get; } = [];
        public CancellationToken Token { get; private set; }
        public Task<SupportTicketResult> CreateTicketAsync(CreateSupportTicketRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Token = cancellationToken;
            Started.TrySetResult();
            return Pending.Task;
        }
        public Task<SupportTicketResult> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DeferredProfiles : IConsoleEnvironmentProfileStore
    {
        public bool DelayNextCapture { get; set; }
        public TaskCompletionSource<ConsoleEnvironmentProfile?> Pending { get; } = new();
        public CancellationToken Token { get; private set; }
        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return DelayNextCapture ? Pending.Task : Task.FromResult<ConsoleEnvironmentProfile?>(null);
        }
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ActivateProfileAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
