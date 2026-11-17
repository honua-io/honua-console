using System.Net;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Honua.Console.IntegrationTests;

// The legacy host approves through the operator's server-session cookie jar: the server refuses a bearer.
public sealed class ConsoleSessionApprovalProposalsClientTests
{
    private static readonly Uri Server = new("https://server.example/");

    [Fact]
    public async Task Approve_UsesTheSessionCookieCsrfAndCapabilityWithoutABearer()
    {
        var server = new ApprovalServer();
        await using var harness = await Harness.CreateAsync(server);
        await harness.OpenServerSessionAsync();

        var result = await harness.Client.ApproveAsync("prop-1", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal(
            ["GET /api/v1/admin/proposals/prop-1/approval-review", "GET /api/v1/admin/auth/csrf", "POST /api/v1/admin/proposals/prop-1/approve"],
            server.Requests.Select(request => request.Line));
        var approve = server.Requests[^1];
        Assert.Equal("capability-3", approve.Capability);
        Assert.Equal("server-csrf", approve.Csrf);
        Assert.All(server.Requests, request => Assert.Null(request.Authorization));
        Assert.All(server.Requests, request => Assert.Contains("honua_admin_session=server-session", request.Cookies, StringComparison.Ordinal));
        Assert.Equal(0, harness.Inner.Approvals);
    }

    [Fact]
    public async Task Approve_ManagerProposalOrMissingSession_KeepsTheInnerClient()
    {
        var server = new ApprovalServer();
        await using var harness = await Harness.CreateAsync(server);

        await harness.Client.ApproveAsync("prop-1", reviewed: null);
        await harness.Client.ApproveAsync("manager:prop-2", reviewed: null);

        Assert.Equal(2, harness.Inner.Approvals);
        Assert.Empty(server.Requests);
    }

    private sealed class Harness(ServiceProvider services, InnerClient inner) : IAsyncDisposable
    {
        public InnerClient Inner => inner;

        public IConsoleProposalsClient Client { get; } = new ConsoleSessionApprovalProposalsClient(inner, services);

        public async Task OpenServerSessionAsync()
        {
            var profile = await services.GetRequiredService<IConsoleEnvironmentProfileStore>().GetActiveProfileAsync();
            services.GetRequiredService<ConsoleServerSessionClientStore>().GetOrCreate("operator-a", profile!);
        }

        public static async Task<Harness> CreateAsync(ApprovalServer server)
        {
            var operators = new OperatorContext();
            var profiles = new OperatorScopedEnvironmentProfileStore(operators);
            await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile { Id = "env", DisplayName = "env", ServerBaseUri = Server });
            await profiles.ActivateProfileAsync("env");
            var sessions = new OperatorScopedAccountSessionStore(operators);
            var partitions = new ConsoleServerSessionClientStore(TimeProvider.System, (_, cookies) =>
            {
                cookies.SetCookies(Server, "honua_admin_session=server-session; Path=/; Secure; HttpOnly");
                server.Cookies = cookies;
                return server;
            });
            var services = new ServiceCollection()
                .AddSingleton<IConsoleOperatorContext>(operators)
                .AddSingleton<IConsoleEnvironmentProfileStore>(profiles)
                .AddSingleton<IConsoleAccountSessionStore>(sessions)
                .AddSingleton(partitions)
                .AddSingleton(new ConsoleServerSessionIdentityBinding(
                    Options.Create(new ConsoleEdgeAuthOptions()), sessions, partitions, TimeProvider.System))
                .BuildServiceProvider();
            return new Harness(services, new InnerClient());
        }

        public async ValueTask DisposeAsync()
        {
            await services.GetRequiredService<ConsoleServerSessionClientStore>().DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private sealed class OperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey => "operator-a";
        public bool HasOperator => true;
        public string RequireOperatorKey() => CurrentOperatorKey;
    }

    private sealed class InnerClient : IConsoleProposalsClient
    {
        public int Approvals { get; private set; }

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default)
        {
            Approvals++;
            return Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden, "inner"));
        }

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId, ConsoleProposalReviewedPlan? reviewed, CancellationToken cancellationToken = default) =>
            ApproveAsync(proposalId, cancellationToken);

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record SeenRequest
    {
        public required string Line { get; init; }
        public string? Authorization { get; init; }
        public string? Csrf { get; init; }
        public string? Capability { get; init; }
        public required string Cookies { get; init; }
    }

    private sealed class ApprovalServer : HttpMessageHandler
    {
        private const string Detail = """
            { "proposalId": "prop-1", "version": 3, "planHash": "plan-3", "kind": "Deploy", "status": "Approved",
              "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:10:00Z" }
            """;

        private const string Review = """
            { "proposal": { "proposalId": "prop-1", "version": 3, "kind": "Deploy", "status": "AwaitingApproval",
                "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:05:00Z" },
              "review": { "tenantId": null, "proposalId": "prop-1", "proposalVersion": 3, "planHash": "plan-3",
                "expiresAt": "2099-01-01T00:00:00Z", "capability": "capability-3" } }
            """;

        public List<SeenRequest> Requests { get; } = [];

        public CookieContainer? Cookies { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new SeenRequest
            {
                Line = $"{request.Method.Method} {path}",
                Authorization = request.Headers.Authorization?.ToString(),
                Csrf = Header(request, "X-Honua-CSRF"),
                Capability = Header(request, "X-Honua-Approval-Capability"),
                Cookies = Cookies?.GetCookieHeader(Server) ?? string.Empty
            });
            var body = path.EndsWith("/approval-review", StringComparison.Ordinal) ? Review
                : path.EndsWith("/csrf", StringComparison.Ordinal) ? """{ "requestToken": "server-csrf" }""" : Detail;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? string.Join("; ", values) : null;
    }
}
