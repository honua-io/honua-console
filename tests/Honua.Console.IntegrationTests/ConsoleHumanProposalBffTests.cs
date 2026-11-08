using Honua.Console.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleHumanProposalBffTests
{
    [Fact]
    public async Task BrowserReviewAndDecision_KeepUpstreamCredentialsPrivateAndConsumeOnce()
    {
        await using var host = await Host.StartAsync();
        var review = await host.OpenReviewAsync();
        Assert.True(review.IsAllowed);
        var value = review.Value!;
        Assert.DoesNotContain("private-capability", JsonSerializer.Serialize(review));
        Assert.DoesNotContain("private-server-session", JsonSerializer.Serialize(review));

        var decision = new ConsoleProposalDecision("proposal", value.Proposal.Version,
            value.Proposal.PlanHash!, value.EnvironmentReference);
        var approved = await host.DecideAsync(decision);
        var duplicate = await host.DecideAsync(decision);

        Assert.True(approved.IsAllowed);
        Assert.False(duplicate.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
        Assert.True(host.SawPrivateCookie);
        Assert.False(host.SawForwardableCredential);
    }

    [Theory]
    [InlineData("csrf")]
    [InlineData("bearer")]
    [InlineData("key")]
    [InlineData("anonymous")]
    [InlineData("profile")]
    public async Task BrowserReview_UntrustedChannel_RefusesBeforeUpstream(string change)
    {
        await using var host = await Host.StartAsync();
        switch (change)
        {
            case "csrf": host.Client.DefaultRequestHeaders.Remove("X-CSRF-Token"); break;
            case "bearer": host.Client.DefaultRequestHeaders.Add("Authorization", "Bearer noninteractive"); break;
            case "key": host.Client.DefaultRequestHeaders.Add("X-API-Key", "noninteractive"); break;
            case "anonymous": host.Authenticated = false; break;
        }
        using var response = await host.Client.PostAsJsonAsync("/api/console/proposals/proposal/review",
            new ConsoleHumanProposalRequest(change == "profile" ? "other" : "environment", ConsoleServerAuthority.Serving, null),
            ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, host.ReviewCalls);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("plan")]
    [InlineData("environment")]
    [InlineData("logout")]
    public async Task BrowserDecision_ReviewAuthorityChanged_RefusesBeforeExecution(string change)
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        decision = change switch
        {
            "version" => decision with { ProposalVersion = decision.ProposalVersion + 1 },
            "plan" => decision with { PlanHash = "changed-plan" },
            "environment" => decision with { EnvironmentReference = "other-environment" },
            _ => decision
        };
        if (change == "logout") host.Sessions.Remove("human", host.Profile);

        var result = await host.DecideAsync(decision);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserDecision_LostResponse_DoesNotResubmitConsumedCapability()
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        host.LoseDecisionResponse = true;

        var uncertain = await host.DecideAsync(decision);
        var retry = await host.DecideAsync(decision);

        Assert.Equal(OperateSectionStatus.Unavailable, uncertain.Status);
        Assert.False(retry.IsAllowed);
        Assert.Equal(1, host.ApprovalCalls);
    }

    [Fact]
    public async Task BrowserRead_ResolvedProposal_UsesPrivateSessionAndDiscardsOldApproval()
    {
        await using var host = await Host.StartAsync();
        var pending = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", pending.Proposal.Version,
            pending.Proposal.PlanHash!, pending.EnvironmentReference);
        host.ProposalStatus = "Executing";

        var resolved = await host.OpenReviewAsync();
        var oldApproval = await host.DecideAsync(decision);

        Assert.True(resolved.IsAllowed);
        Assert.Equal("Executing", resolved.Value!.Proposal.Status);
        Assert.Empty(resolved.Value.EnvironmentReference);
        Assert.False(oldApproval.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
        Assert.True(host.SawPrivateCookie);
    }

    [Fact]
    public async Task BrowserDecision_EnvironmentChangesDuringIdentityCheck_RefusesBeforeSubmission()
    {
        await using var host = await Host.StartAsync();
        var review = (await host.OpenReviewAsync()).Value!;
        var decision = new ConsoleProposalDecision("proposal", review.Proposal.Version,
            review.Proposal.PlanHash!, review.EnvironmentReference);
        host.SwitchDuringIdentityCheck = true;

        var result = await host.DecideAsync(decision);

        Assert.False(result.IsAllowed);
        Assert.Equal(0, host.ApprovalCalls);
    }

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _browser = null!;
        private WebApplication _upstream = null!;
        public HttpClient Client { get; private set; } = null!;
        public ConsoleEnvironmentProfile Profile { get; private set; } = null!;
        public ConsoleServerSessionClientStore Sessions { get; private set; } = null!;
        public bool Authenticated { get; set; } = true;
        public string ProposalStatus { get; set; } = "AwaitingApproval";
        public bool LoseDecisionResponse { get; set; }
        public bool SwitchDuringIdentityCheck { get; set; }
        private InMemoryConsoleEnvironmentProfileStore _profiles = null!;
        private const string IdentityIssuer = "https://identity.example/";
        public int ReviewCalls { get; private set; }
        public int ApprovalCalls { get; private set; }
        public bool SawPrivateCookie { get; private set; }
        public bool SawForwardableCredential { get; private set; }

        public static async Task<Host> StartAsync()
        {
            var host = new Host();
            await host.StartUpstreamAsync();
            host.InitializePrivateSession();
            await host.StartBrowserAsync();
            return host;
        }

        private async Task StartUpstreamAsync()
        {
            _upstream = Builder().Build();
            _upstream.MapGet("/api/v1/admin/auth/session", ReadIdentityAsync);
            _upstream.MapGet("/api/v1/admin/proposals/proposal/approval-review", OpenPrivateReview);
            _upstream.MapGet("/api/v1/admin/proposals/proposal", ReadProposal);
            _upstream.MapPost("/api/v1/admin/proposals/proposal/approve", DecideProposal);
            await _upstream.StartAsync();
        }

        private async Task<IResult> ReadIdentityAsync(HttpContext context)
        {
            Observe(context);
            if (SwitchDuringIdentityCheck)
            {
                SwitchDuringIdentityCheck = false;
                var other = Profile with { Id = "other-environment" };
                await _profiles.UpsertProfileAsync(other);
                await _profiles.ActivateProfileAsync(other.Id);
            }
            return Results.Json(new ConsoleServerAuthenticatedSessionWire
            {
                IsAuthenticated = true, ProviderKey = "oidc", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Claims = [new() { Type = "iss", Value = IdentityIssuer }, new() { Type = "sub", Value = "human" }]
            }, ConsoleServerSessionIdentityJsonContext.Default.ConsoleServerAuthenticatedSessionWire);
        }

        private IResult OpenPrivateReview(HttpContext context)
        {
            ReviewCalls++;
            Observe(context);
            context.Response.Headers.CacheControl = "no-store";
            if (ProposalStatus != "AwaitingApproval") return Results.Conflict();
            return Results.Json(new ConsolePrivateProposalReviewResponse(
                new ProposalDetailWire { ProposalId = "proposal", Status = "AwaitingApproval", Version = 7, PlanHash = "plan" },
                new ConsoleProposalHumanReviewWire("proposal", 7, "plan", "private-capability", DateTimeOffset.UtcNow.AddMinutes(5))),
                ConsoleHumanProposalJsonContext.Default.ConsolePrivateProposalReviewResponse);
        }

        private IResult ReadProposal(HttpContext context)
        {
            Observe(context);
            return Results.Json(new ProposalDetailWire
            {
                ProposalId = "proposal", Status = ProposalStatus, Version = 7, PlanHash = "plan"
            }, ConsoleHumanProposalJsonContext.Default.ProposalDetailWire);
        }

        private IResult DecideProposal(HttpContext context)
        {
            ApprovalCalls++;
            Observe(context);
            Assert.Equal("private-capability", context.Request.Headers["X-Honua-Approval-Capability"].ToString());
            if (LoseDecisionResponse) { context.Abort(); return Results.Empty; }
            return Results.Json(new ProposalDetailWire { ProposalId = "proposal", Status = "Executing", Version = 8, PlanHash = "plan" },
                ConsoleHumanProposalJsonContext.Default.ProposalDetailWire);
        }

        private void InitializePrivateSession()
        {
            Profile = new ConsoleEnvironmentProfile { Id = "environment", ServerBaseUri = Address(_upstream) };
            Sessions = new ConsoleServerSessionClientStore(TimeProvider.System);
            var partition = Sessions.GetOrCreate("human", Profile);
            partition.AuthenticatedProviderKey = "oidc";
            partition.Cookies.SetCookies(Profile.ServerBaseUri,
                "honua_admin_session=private-server-session; Path=/; HttpOnly");
            _profiles = new InMemoryConsoleEnvironmentProfileStore([Profile], activeProfileId: Profile.Id);
        }

        private async Task StartBrowserAsync()
        {
            var identity = new ConsoleServerSessionIdentityBinding(
                Options.Create(new ConsoleEdgeAuthOptions { ServerSessionIssuer = IdentityIssuer }),
                new InMemoryConsoleAccountSessionStore(), Sessions, TimeProvider.System);
            var builder = Builder();
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(_profiles);
            builder.Services.AddSingleton(new ConsoleHumanProposalSession(
                new PartitionedConsoleOperatorBearerExchange(new HumanOperator(), Sessions, identity), _profiles, TimeProvider.System));
            _browser = builder.Build();
            _browser.Use(async (context, next) =>
            {
                context.User = Authenticated
                    ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "human")], "browser-cookie"))
                    : new ClaimsPrincipal();
                await next();
            });
            _browser.MapConsoleHumanProposalBff();
            await _browser.StartAsync();
            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Address(_browser) };
            using var bootstrap = await Client.GetAsync("/api/console/proposals/session");
            var session = await bootstrap.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalBrowserSession);
            Client.DefaultRequestHeaders.Add("X-CSRF-Token", session!.CsrfToken);
        }

        public async Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync()
        {
            using var response = await Client.PostAsJsonAsync("/api/console/proposals/proposal/review",
                new ConsoleHumanProposalRequest(Profile.Id, ConsoleServerAuthority.Serving, null),
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            return (await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultConsoleHumanProposalReview))!;
        }

        public async Task<OperateSectionResult<ProposalDetailWire>> DecideAsync(ConsoleProposalDecision decision)
        {
            using var response = await Client.PostAsJsonAsync("/api/console/proposals/proposal/approve",
                new ConsoleHumanProposalRequest(Profile.Id, ConsoleServerAuthority.Serving, decision),
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire))!;
        }

        private void Observe(HttpContext context)
        {
            SawPrivateCookie |= context.Request.Cookies["honua_admin_session"] == "private-server-session";
            SawForwardableCredential |= context.Request.Headers.ContainsKey("Authorization") || context.Request.Headers.ContainsKey("X-API-Key");
            Assert.Equal("private-server-session", context.Request.Cookies["honua_admin_session"]);
        }

        private static WebApplicationBuilder Builder()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            return builder;
        }

        private static Uri Address(WebApplication app) => new(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single());

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _browser.DisposeAsync();
            await _upstream.DisposeAsync();
            await Sessions.DisposeAsync();
        }
    }

    private sealed class HumanOperator : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "human";
        public bool HasOperator => true;
        public string RequireOperatorKey() => "human";
    }
}
