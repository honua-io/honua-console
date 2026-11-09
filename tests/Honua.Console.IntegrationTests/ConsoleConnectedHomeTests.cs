using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleConnectedHomeTests
{
    [Fact]
    public void ActiveEnvironment_LeadsWithApprovalsHealthAndRecentVisits_AndKeepsSetupAvailable()
    {
        using var context = CreateContext();
        var home = context.Render<ConsoleHomePage>();

        home.WaitForAssertion(() =>
        {
            Assert.Equal("Welcome", home.Find("h1").TextContent);
            Assert.Contains("Production", home.Find("[data-home-environment]").TextContent);
            Assert.Empty(home.FindAll(".console-home-operations"));
            Assert.NotNull(home.Find("[data-open-inbox]"));
            Assert.NotNull(home.Find("[data-ops-summary-strip]"));
            Assert.NotNull(home.Find("[data-home-recent]"));
            Assert.False(home.Find(".console-home-setup").HasAttribute("open"));
            Assert.Equal("/setup/local", home.Find("[data-start-local]").GetAttribute("href"));
            Assert.Equal("/environments/new", home.Find("[data-connect-existing]").GetAttribute("href"));
        });
        CatalogAdminPreview.Capture("home", home.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RolloutSwitch_PreservesEnvironmentActionsAndPartialQueueTruth(bool enabled)
    {
        var result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty,
            partialResult: true, message: "The supplementary proposal source could not be read.");
        using var context = CreateContext(result);
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        var home = context.Render<ConsoleHomePage>();

        home.WaitForAssertion(() =>
        {
            Assert.Single(home.FindAll("h1"));
            Assert.Contains("Production", home.Find("[data-home-environment]").TextContent);
            Assert.Equal("/inbox", home.Find("[data-open-inbox]").GetAttribute("href"));
            Assert.Equal("/environments", home.Find(".console-home-administration a[href='/environments']").GetAttribute("href"));
            Assert.Contains("supplementary proposal source", home.Find("[data-home-inbox-partial]").TextContent);
            Assert.Contains("in returned sources", home.Find("[data-home-total-count]").TextContent);
            Assert.Equal(enabled ? 1 : 0, home.FindAll(".console-metric-tile").Count);
            Assert.Equal(enabled ? 1 : 0, home.FindAll(".console-page-heading-copy").Count);
            if (enabled)
            {
                Assert.Equal("0", home.Find("[data-home-awaiting-count] dd").TextContent);
                Assert.Contains("returned sources", home.Find("[data-home-awaiting-count] dt").TextContent);
            }
            else
            {
                Assert.Equal("0", home.Find("[data-home-awaiting-count] strong").TextContent);
            }
        });
        CatalogAdminPreview.Capture($"home-v4-{enabled.ToString().ToLowerInvariant()}",
            $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\"><main class=\"console-main\">{home.Markup}</main></div>");
    }

    [Fact]
    public void V4CompleteQueue_UsesMeasuredPendingCountAndRetainsTotalAsContext()
    {
        var snapshot = FakeProposalFactory.Snapshot(
            FakeProposalFactory.Summary("pending", ConsoleProposalKind.Deploy),
            FakeProposalFactory.Summary("complete", ConsoleProposalKind.Deploy, ConsoleProposalStatus.Completed));
        using var context = CreateContext(OperateSectionResult<ApprovalInboxSnapshot>.Allowed(snapshot));
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        var home = context.Render<ConsoleHomePage>();

        home.WaitForAssertion(() =>
        {
            Assert.Equal("1", home.Find("[data-home-awaiting-count] dd").TextContent);
            Assert.Equal("Awaiting approval", home.Find("[data-home-awaiting-count] dt").TextContent);
            Assert.Contains("2 in the queue", home.Find("[data-home-total-count]").TextContent);
            Assert.Empty(home.FindAll("[data-home-inbox-partial]"));
            Assert.Equal("/inbox", home.Find("[data-open-inbox]").GetAttribute("href"));
        });
    }

    [Fact]
    public void PartialEmptyQueue_ShowsSourceWarningWithoutClaimingNoPendingWork()
    {
        var result = OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty,
            partialResult: true, message: "The supplementary proposal source could not be read.");
        using var context = CreateContext(result);
        var home = context.Render<ConsoleHomePage>();

        home.WaitForAssertion(() =>
        {
            Assert.Contains("supplementary proposal source", home.Find("[data-home-inbox-partial]").TextContent);
            Assert.Contains("No pending approvals in the sources returned.", home.Markup);
            Assert.DoesNotContain("No agent-proposed operations are awaiting a decision right now.", home.Markup);
            Assert.Contains("in returned sources", home.Find("[data-home-total-count]").TextContent);
            Assert.Contains("Partial queue", home.Find("[data-summary-tile='approvals']").TextContent);
        });
    }

    [Theory]
    [InlineData(OperateSectionStatus.Forbidden)]
    [InlineData(OperateSectionStatus.Unsupported)]
    public void DeniedQueue_PreservesItsStateWithoutInventingAZeroOrRequiringSignIn(OperateSectionStatus status)
    {
        using var context = CreateContext(OperateSectionResult<ApprovalInboxSnapshot>.Denied(status, ""));
        var home = context.Render<ConsoleHomePage>();

        home.WaitForAssertion(() =>
        {
            Assert.Contains(OperateSectionPresentation.FallbackMessage(status), home.Markup);
            Assert.Empty(home.FindAll("[data-home-awaiting-count]"));
            Assert.Equal("—", home.Find("[data-summary-value='approvals']").TextContent.Trim());
            Assert.DoesNotContain("Sign in or review", home.Markup);
            Assert.Empty(home.FindAll("[data-home-connection-state]"));
        });
    }

    [Fact]
    public async Task SameUrlEnvironmentActivation_ReplacesTheVisibleEnvironmentAndItsReadLifetime()
    {
        using var context = CreateContext();
        var home = context.Render<ConsoleHomePage>();
        var profiles = context.Services.GetRequiredService<IConsoleEnvironmentProfileStore>();

        await home.InvokeAsync(() => profiles.UpsertProfileAsync(Profile("test", "Test")));
        await home.InvokeAsync(() => profiles.ActivateProfileAsync("test"));

        home.WaitForAssertion(() =>
        {
            Assert.Contains("Test", home.Find("[data-home-environment]").TextContent);
            Assert.DoesNotContain("Production", home.Find("[data-home-environment]").TextContent);
            Assert.Single(home.FindAll("h1"));
        });
    }

    [Fact]
    public async Task SameAuthorityRefresh_DisposesPreviousQueueAndSummarySubscriptions()
    {
        using var context = CreateContext();
        var updates = new SubscriptionTracker();
        context.Services.AddSingleton<IConsoleProposalRealtimeClient>(updates);
        var home = context.Render<ConsoleHomePage>();
        home.WaitForAssertion(() => Assert.Equal(2, updates.Starts));
        var profiles = context.Services.GetRequiredService<IConsoleEnvironmentProfileStore>();
        await home.InvokeAsync(() => profiles.UpsertProfileAsync(Profile("production", "Production")));
        home.WaitForAssertion(() =>
        {
            Assert.Equal(4, updates.Starts);
            Assert.Equal(2, updates.Removed);
            Assert.Equal(2, updates.Subscribers);
        });
    }

    private sealed class SubscriptionTracker : IConsoleProposalRealtimeClient
    {
        public int Starts { get; private set; }
        public int Removed { get; private set; }
        public int Subscribers { get; private set; }
        public bool IsConnected => false;
        public event Action<ConsoleProposalEvent>? ProposalChanged
        {
            add { Subscribers++; }
            remove { Subscribers--; Removed++; }
        }
        public Task StartAsync(CancellationToken token = default) { Starts++; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static BunitContext CreateContext(OperateSectionResult<ApprovalInboxSnapshot>? result = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions());
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:EmbedJsStudio"] = "true" }).Build());
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
            new InMemoryConsoleEnvironmentProfileStore([Profile("production", "Production")]));
        context.Services.AddSingleton<IConsoleApprovalInboxClient>(new InboxClient(result ??
            OperateSectionResult<ApprovalInboxSnapshot>.Allowed(ApprovalInboxSnapshot.Empty)));
        return context;
    }

    private static ConsoleEnvironmentProfile Profile(string id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        ServerBaseUri = new Uri($"https://{id}.example.test/"),
        Account = new ConsoleAccountBinding { AccountId = "current-operator" }
    };

    private sealed class InboxClient(OperateSectionResult<ApprovalInboxSnapshot> result) : IConsoleApprovalInboxClient
    {
        public Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
            string? status = null, string? kind = null, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
