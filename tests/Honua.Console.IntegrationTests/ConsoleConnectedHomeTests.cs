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

    private static BunitContext CreateContext(OperateSectionResult<ApprovalInboxSnapshot>? result = null)
    {
        var context = new BunitContext();
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
