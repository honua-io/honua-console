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
            [
                "GET /api/v1/admin/proposals/prop-1/approval-review",
                "GET /api/v1/admin/proposals/prop-1/approval-review",
                "GET /api/v1/admin/auth/csrf",
                "POST /api/v1/admin/proposals/prop-1/approve"
            ],
            server.Requests.Select(request => request.Line));
        var approve = server.Requests[^1];
        Assert.Equal("capability-3", approve.Capability);
        Assert.Equal("server-csrf", approve.Csrf);
        Assert.All(server.Requests, request => Assert.Null(request.Authorization));
        Assert.All(server.Requests, request => Assert.Contains("honua_admin_session=server-session", request.Cookies, StringComparison.Ordinal));
        Assert.Equal(0, harness.Inner.Approvals);
    }

    [Fact]
    public async Task Approve_WithoutServerSession_KeepsTheInnerClient()
    {
        var server = new ApprovalServer();
        await using var harness = await Harness.CreateAsync(server);

        await harness.Client.ApproveAsync("prop-1", reviewed: null);

        Assert.Equal(1, harness.Inner.Approvals);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Approve_ManagerProposal_KeepsTheInnerClientEvenWithASession()
    {
        var server = new ApprovalServer();
        await using var harness = await Harness.CreateAsync(server);
        await harness.OpenServerSessionAsync();

        await harness.Client.ApproveAsync("manager:prop-2", reviewed: null);

        Assert.Equal(1, harness.Inner.Approvals);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Approve_ManagerProposal_UsesTheManagerSessionWithoutABearer()
    {
        var server = new ApprovalServer();
        var manager = new Uri("https://manager.example/");
        await using var harness = await Harness.CreateAsync(server, manager);
        await harness.OpenServerSessionAsync();
        harness.OpenManagerSession(manager);

        var result = await harness.Client.ApproveAsync("manager:prop-2", new ConsoleProposalReviewedPlan(3, "plan-3"));

        Assert.Equal(OperateSectionStatus.Allowed, result.Status);
        Assert.Equal("manager:prop-2", result.Value!.ProposalId);
        Assert.Equal(0, harness.Inner.Approvals);
        Assert.Equal(
            [
                "GET /api/v1/admin/proposals/prop-2/approval-review",
                "GET /api/v1/admin/proposals/prop-2/approval-review",
                "GET /api/v1/admin/auth/csrf",
                "POST /api/v1/admin/proposals/prop-2/approve"
            ],
            server.Requests.Select(request => request.Line));
        Assert.All(server.Requests, request => Assert.Equal("manager.example", request.Host));
        Assert.All(server.Requests, request => Assert.Null(request.Authorization));
        Assert.All(server.Requests, request => Assert.Contains("honua_admin_session=manager-session", request.Cookies, StringComparison.Ordinal));
        Assert.All(server.Requests, request => Assert.DoesNotContain("server-session", request.Cookies, StringComparison.Ordinal));
        Assert.Equal("capability-3", server.Requests[^1].Capability);
        Assert.Equal("server-csrf", server.Requests[^1].Csrf);
    }

    [Fact]
    public async Task Approve_ManagerEndpointWithoutSession_KeepsTheInnerClient()
    {
        var server = new ApprovalServer();
        await using var harness = await Harness.CreateAsync(server, new Uri("https://manager.example/"));
        await harness.OpenServerSessionAsync();

        await harness.Client.ApproveAsync("manager:prop-2", reviewed: null);

        Assert.Equal(1, harness.Inner.Approvals);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Approve_ManagerReviewMissing_KeepsTheInnerClient()
    {
        var server = new ApprovalServer { ReviewMissing = true };
        var manager = new Uri("https://manager.example/");
        await using var harness = await Harness.CreateAsync(server, manager);
        await harness.OpenServerSessionAsync();
        harness.OpenManagerSession(manager);

        await harness.Client.ApproveAsync("manager:prop-2", reviewed: null);

        Assert.Equal(1, harness.Inner.Approvals);
        Assert.Equal(
            ["GET /api/v1/admin/proposals/prop-2/approval-review"],
            server.Requests.Select(request => request.Line));
        Assert.Equal("manager.example", server.Requests[0].Host);
    }

    [Fact]
    public async Task Approve_ServerWithoutApprovalReview_KeepsTheBearerPath()
    {
        var server = new ApprovalServer { ReviewMissing = true };
        await using var harness = await Harness.CreateAsync(server);
        await harness.OpenServerSessionAsync();

        await harness.Client.ApproveAsync("prop-1", reviewed: null);

        Assert.Equal(1, harness.Inner.Approvals);
        Assert.Equal(["GET /api/v1/admin/proposals/prop-1/approval-review"], server.Requests.Select(request => request.Line));
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

        public void OpenManagerSession(Uri manager)
        {
            var partition = services.GetRequiredService<ConsoleServerSessionClientStore>()
                .GetOrCreate("operator-a", "env", manager, out _);
            partition.Cookies.SetCookies(manager, "honua_admin_session=manager-session; Path=/; Secure; HttpOnly");
            partition.AuthenticatedProviderKey = "oidc";
        }

        public static Task<Harness> CreateAsync(ApprovalServer server) => CreateAsync(server, manager: null);

        public static async Task<Harness> CreateAsync(ApprovalServer server, Uri? manager)
        {
            var operators = new OperatorContext();
            var profiles = new OperatorScopedEnvironmentProfileStore(operators);
            await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile { Id = "env", DisplayName = "env", ServerBaseUri = Server });
            await profiles.ActivateProfileAsync("env");
            var sessions = new OperatorScopedAccountSessionStore(operators);
            var partitions = new ConsoleServerSessionClientStore(TimeProvider.System, (_, cookies) =>
            {
                cookies.SetCookies(Server, "honua_admin_session=server-session; Path=/; Secure; HttpOnly");
                return new JarHandler(server, cookies);
            });
            var services = new ServiceCollection()
                .AddSingleton<IConsoleOperatorContext>(operators)
                .AddSingleton<IConsoleEnvironmentProfileStore>(profiles)
                .AddSingleton<IConsoleAccountSessionStore>(sessions)
                .AddSingleton(partitions)
                .AddSingleton(new ConsoleServerSessionIdentityBinding(
                    Options.Create(new ConsoleEdgeAuthOptions()), sessions, partitions, TimeProvider.System));
            if (manager is not null)
            {
                services.AddSingleton<IConsoleManagerEndpoint>(new FixedManager(manager));
            }

            return new Harness(services.BuildServiceProvider(), new InnerClient());
        }

        public async ValueTask DisposeAsync()
        {
            await services.GetRequiredService<ConsoleServerSessionClientStore>().DisposeAsync();
            await services.DisposeAsync();
        }
    }

    private sealed class FixedManager(Uri manager) : IConsoleManagerEndpoint
    {
        public Uri? Resolve(Uri servingBaseUri) => manager;
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
        public string Host { get; init; } = string.Empty;
    }

    private sealed class JarHandler(ApprovalServer server, CookieContainer cookies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            server.DispatchAsync(request, cookies, cancellationToken);
    }

    private sealed class ApprovalServer
    {
        private static string ProposalIdFromPath(string path)
        {
            const string marker = "/api/v1/admin/proposals/";
            var start = path.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return "prop-1";
            }

            var id = path[(start + marker.Length)..];
            var slash = id.IndexOf('/');
            return slash < 0 ? id : id[..slash];
        }

        private static string DetailBody(string proposalId) =>
            $$"""
            { "proposalId": "{{proposalId}}", "version": 3, "planHash": "plan-3", "kind": "Deploy", "status": "Approved",
              "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:10:00Z" }
            """;

        private static string ReviewBody(string proposalId) =>
            $$"""
            { "proposal": { "proposalId": "{{proposalId}}", "version": 3, "kind": "Deploy", "status": "AwaitingApproval",
                "summary": "Upgrade", "createdAt": "2026-06-28T10:00:00Z", "updatedAt": "2026-06-28T10:05:00Z" },
              "review": { "tenantId": null, "proposalId": "{{proposalId}}", "proposalVersion": 3, "planHash": "plan-3",
                "expiresAt": "2099-01-01T00:00:00Z", "capability": "capability-3" } }
            """;

        public List<SeenRequest> Requests { get; } = [];

        public bool ReviewMissing { get; init; }

        public Task<HttpResponseMessage> DispatchAsync(
            HttpRequestMessage request,
            CookieContainer cookies,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new SeenRequest
            {
                Line = $"{request.Method.Method} {path}",
                Authorization = request.Headers.Authorization?.ToString(),
                Csrf = Header(request, "X-Honua-CSRF"),
                Capability = Header(request, "X-Honua-Approval-Capability"),
                Cookies = cookies.GetCookieHeader(request.RequestUri!),
                Host = request.RequestUri.Host
            });
            if (ReviewMissing && path.EndsWith("/approval-review", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var proposalId = ProposalIdFromPath(path);
            var body = path.EndsWith("/approval-review", StringComparison.Ordinal) ? ReviewBody(proposalId)
                : path.EndsWith("/csrf", StringComparison.Ordinal) ? """{ "requestToken": "server-csrf" }""" : DetailBody(proposalId);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? string.Join("; ", values) : null;
    }
}
