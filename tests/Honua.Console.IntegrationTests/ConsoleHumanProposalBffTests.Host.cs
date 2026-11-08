using Honua.Console.Contracts;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
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

public sealed partial class ConsoleHumanProposalBffTests
{
    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _browser = null!;
        private WebApplication _upstream = null!;
        public HttpClient Client { get; private set; } = null!;
        public ConsoleEnvironmentProfile Profile { get; private set; } = null!;
        public ConsoleServerSessionClientStore Sessions { get; private set; } = null!;
        public bool Authenticated { get; set; } = true;
        public string AuthenticationScheme { get; set; } = "browser-cookie";
        public string ProposalStatus { get; set; } = "AwaitingApproval";
        public bool LoseDecisionResponse { get; set; }
        public bool SwitchDuringIdentityCheck { get; set; }
        public string? ReviewTenantId { get; set; }
        public bool OmitReviewTenant { get; set; }
        public TimeProvider Clock { get; } = new FixtureClock();
        public TimeSpan ReviewClockSkew { get; set; }
        public string? ReviewMutation { get; set; }
        public HttpStatusCode ReviewStatusCode { get; set; } = HttpStatusCode.OK;
        public int ReadCalls { get; private set; }
        private string _pathBase = string.Empty;
        private readonly ManagementEndpoint _management = new();
        private InMemoryConsoleEnvironmentProfileStore _profiles = null!;
        private const string IdentityIssuer = "https://identity.example/";
        public int ReviewCalls { get; private set; }
        public int ApprovalCalls { get; private set; }
        public bool SawPrivateCookie { get; private set; }
        public bool SawForwardableCredential { get; private set; }

        public static async Task<Host> StartAsync(string tenant = "", string accountTenant = "", string pathBase = "")
        {
            var host = new Host { _pathBase = pathBase };
            await host.StartUpstreamAsync();
            host.InitializePrivateSession(tenant, accountTenant);
            await host.StartBrowserAsync();
            return host;
        }

        private async Task StartUpstreamAsync()
        {
            _upstream = Builder().Build();
            MapUpstreamRoutes(_pathBase);
            MapUpstreamRoutes(_pathBase + "/management");
            await _upstream.StartAsync();
        }

        private void MapUpstreamRoutes(string pathBase)
        {
            _upstream.MapGet(pathBase + "/api/v1/admin/auth/session", (Func<HttpContext, Task<IResult>>)ReadIdentityAsync);
            _upstream.MapGet(pathBase + "/api/v1/admin/proposals/proposal/approval-review", OpenPrivateReview);
            _upstream.MapGet(pathBase + "/api/v1/admin/proposals/proposal", ReadProposal);
            _upstream.MapPost(pathBase + "/api/v1/admin/proposals/proposal/approve", DecideProposal);
        }

        public void ConfigureManagement()
        {
            var managerBaseUri = new Uri(Profile.ServerBaseUri, "management/");
            _management.Target = managerBaseUri;
            var partition = Sessions.GetOrCreate("human", Profile with { ServerBaseUri = managerBaseUri });
            partition.AuthenticatedProviderKey = "oidc";
            partition.Cookies.SetCookies(managerBaseUri, "honua_admin_session=private-server-session; Path=/; HttpOnly");
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
            if (ReviewMutation != "missing-no-store") context.Response.Headers.CacheControl = "no-store";
            if (ReviewStatusCode != HttpStatusCode.OK) return Results.StatusCode((int)ReviewStatusCode);
            if (ProposalStatus != "AwaitingApproval") return Results.Conflict();
            var result = new ConsolePrivateProposalReviewResponse(
                new ProposalDetailWire { ProposalId = "proposal", Status = "AwaitingApproval", Version = 7, PlanHash = "plan" },
                new ConsoleProposalHumanReviewWire("proposal", 7, "plan", "private-capability", Clock.GetUtcNow().AddMinutes(5) + ReviewClockSkew));
            var json = JsonNode.Parse(JsonSerializer.Serialize(result,
                ConsoleHumanProposalJsonContext.Default.ConsolePrivateProposalReviewResponse))!;
            if (OmitReviewTenant) json["review"]!.AsObject().Remove("tenantId");
            else json["review"]!["tenantId"] = ReviewTenantId;
            MutateReview(json);
            return Results.Text(json.ToJsonString(), "application/json");
        }

        private void MutateReview(JsonNode json)
        {
            var review = json["review"]!;
            switch (ReviewMutation)
            {
                case "expired": review["expiresAt"] = Clock.GetUtcNow().AddSeconds(-1); break;
                case "long-lifetime": review["expiresAt"] = Clock.GetUtcNow().AddMinutes(6); break;
                case "oversized-capability": review["capability"] = new string('x', 16385); break;
                case "resolved-status": json["proposal"]!["status"] = "Executing"; break;
                case "version": review["proposalVersion"] = 8; break;
                case "plan": review["planHash"] = "different-plan"; break;
                case "proposal-id": review["proposalId"] = "other-proposal"; break;
            }
        }

        private IResult ReadProposal(HttpContext context)
        {
            ReadCalls++;
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

        private void InitializePrivateSession(string tenant, string accountTenant)
        {
            Profile = new ConsoleEnvironmentProfile
            {
                Id = "environment", ServerBaseUri = new Uri(Address(_upstream), _pathBase + "/"), TenantId = tenant,
                Account = new ConsoleAccountBinding { TenantId = accountTenant }
            };
            ReviewTenantId = string.IsNullOrEmpty(tenant) ? null : tenant;
            Sessions = new ConsoleServerSessionClientStore(Clock);
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
                new InMemoryConsoleAccountSessionStore(), Sessions, Clock);
            var builder = Builder();
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-Token");
            builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(_profiles);
            builder.Services.AddSingleton(new ConsoleHumanProposalSession(
                new PartitionedConsoleOperatorBearerExchange(new HumanOperator(), Sessions, identity), _profiles, Clock, _management));
            _browser = builder.Build();
            _browser.Use(async (context, next) =>
            {
                context.User = Authenticated
                    ? new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "human")], AuthenticationScheme))
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

        public async Task<OperateSectionResult<ConsoleHumanProposalReview>> OpenReviewAsync(ConsoleServerAuthority authority = ConsoleServerAuthority.Serving)
        {
            using var response = await Client.PostAsJsonAsync("/api/console/proposals/proposal/review",
                new ConsoleHumanProposalRequest(Profile.Id, authority, null),
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            return (await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultConsoleHumanProposalReview))!;
        }

        public async Task<OperateSectionResult<ProposalDetailWire>> DecideAsync(
            ConsoleProposalDecision decision, ConsoleServerAuthority authority = ConsoleServerAuthority.Serving)
        {
            using var response = await Client.PostAsJsonAsync("/api/console/proposals/proposal/approve",
                new ConsoleHumanProposalRequest(Profile.Id, authority, decision),
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire))!;
        }

        public async Task<OperateSectionResult<ProposalDetailWire>> ReadStatusAsync()
        {
            using var response = await Client.PostAsJsonAsync("/api/console/proposals/proposal/status",
                new ConsoleHumanProposalRequest(Profile.Id, ConsoleServerAuthority.Serving, null),
                ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync(
                ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire))!;
        }

        public DateTimeOffset? TakeCachedExpiry() => Sessions.TryGet("human", Profile, out var partition)
            ? partition!.ProposalReviews.Take("proposal", 7, "plan")?.ExpiresAt : null;

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

    private sealed class FixtureClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class ManagementEndpoint : IConsoleManagerEndpoint
    {
        public Uri? Target { get; set; }
        public Uri? Resolve(Uri servingBaseUri) => Target ?? servingBaseUri;
    }

    private sealed class HumanOperator : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "human";
        public bool HasOperator => true;
        public string RequireOperatorKey() => "human";
    }
}
