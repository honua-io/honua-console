using Bunit;
using Honua.Console.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Honua.Console.Shell.Components;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class BrowserDeployApprovalTests
{
    private static readonly Uri Api = new("https://honua.example/");
    private static readonly Uri App = new("https://honua.example/console/");

    [Fact]
    public async Task ReviewDoesNotSubmitAndExplicitDecisionUsesCookieCsrfAndCapability()
    {
        var session = Session();
        var server = new ReviewServer();
        var client = Client(session, server);
        var opened = await client.OpenSubmitReviewAsync("deploy-1");
        Assert.True(opened.IsAllowed, opened.Message);
        Assert.Single(server.Requests);
        Assert.Equal("reviewed-revision", opened.Value!.Operation.DesiredRevision);
        Assert.DoesNotContain("issued-capability", opened.Value.ToString());

        var result = await client.SubmitAsync(opened.Value, "Reviewed");
        Assert.True(result.IsAllowed, result.Message);
        Assert.Equal("Submitted", result.Value!.RawStatus);
        var decision = server.Requests[1];
        Assert.Equal("POST /api/v1/admin/deploy/operations/deploy-1/submit", decision.Line);
        Assert.Equal("issued-capability", decision.Capability);
        Assert.Equal("issued-csrf", decision.Csrf);
        Assert.All(server.Requests, r => { Assert.Null(r.Authorization); Assert.Null(r.ApiKey); });
        Assert.False((await client.SubmitAsync(opened.Value, null)).IsAllowed);
        Assert.Equal(2, server.Requests.Count);
    }

    [Theory]
    [InlineData("missing-csrf")]
    [InlineData("changed-session")]
    public async Task MissingCsrfOrChangedSessionPreventsDecision(string failure)
    {
        var session = Session(failure == "missing-csrf" ? null : "issued-csrf");
        var server = new ReviewServer();
        var client = Client(session, server);
        var opened = await client.OpenSubmitReviewAsync("deploy-1");
        Assert.True(opened.IsAllowed, opened.Message);
        if (failure == "changed-session") session.Set(Principal("another-human"), new ConsoleSessionCredentials(CsrfToken: "other-csrf"));

        Assert.False((await client.SubmitAsync(opened.Value!, null)).IsAllowed);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task ChangedOperationRequiresNewReviewWithoutAutomaticRetry()
    {
        var server = new ReviewServer { DecisionStatus = HttpStatusCode.Conflict };
        var client = Client(Session(), server);
        var opened = await client.OpenSubmitReviewAsync("deploy-1");
        var result = await client.SubmitAsync(opened.Value!, null);
        Assert.False(result.IsAllowed);
        Assert.Contains("Review the deployment again", result.Message);
        Assert.Equal(2, server.Requests.Count);
    }

    [Theory]
    [InlineData(-1, HttpStatusCode.OK)]
    [InlineData(5, HttpStatusCode.NotFound)]
    public async Task ExpiredOrUnavailableReviewDoesNotOfferApproval(int lifetimeMinutes, HttpStatusCode status)
    {
        var server = new ReviewServer { LifetimeMinutes = lifetimeMinutes, ReviewStatus = status };
        var result = await Client(Session(), server).OpenSubmitReviewAsync("deploy-1");
        Assert.False(result.IsAllowed);
        Assert.Single(server.Requests);
    }

    [Fact]
    public void PanelDisplaysReviewedOperationBeforeSeparateApproval()
    {
        var server = new ReviewServer();
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleDeployApprovalClient>(Client(Session(), server));
        var panel = context.Render<OperateDeploymentApprovalPanel>(p => p
            .Add(x => x.OperationId, "deploy-1").Add(x => x.PollInterval, (TimeSpan?)null));
        panel.WaitForAssertion(() => Assert.Contains("original-revision", panel.Markup));
        panel.Find("button.console-button").Click();
        panel.WaitForAssertion(() => Assert.Contains("reviewed-revision", panel.Markup));
        Assert.Contains("Approve &amp; submit", panel.Markup);
        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.StartsWith("GET ", r.Line));
        panel.Find("button.console-button").Click();
        panel.WaitForAssertion(() => Assert.Contains("Approved", panel.Markup));
        Assert.Equal(3, server.Requests.Count);
        Assert.StartsWith("POST ", server.Requests[2].Line);
    }

    private static ConsoleBrowserSession Session(string? csrfToken = "issued-csrf")
    {
        var session = new ConsoleBrowserSession();
        session.Set(Principal("human"), new ConsoleSessionCredentials(CsrfToken: csrfToken));
        return session;
    }

    private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    private static HttpConsoleDeployApprovalClient Client(ConsoleBrowserSession session, ReviewServer server, InMemoryConsoleEnvironmentProfileStore? profiles = null)
    {
        var http = new HttpClient(new BrowserApiHandler(new BrowserApiOrigins(Api, App), session, BrowserApiAccess.Authenticated)
        { InnerHandler = server })
        { BaseAddress = Api };
        profiles ??= new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "live", ServerBaseUri = Api }], activeProfileId: "live");
        return new(http, profiles, new InMemoryConsoleAccountSessionStore(), "unused-admin-key",
            new BrowserOperatorBearerProvider(session, Api), authority: new BrowserAuthorityBindingValidator(session, Api));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AuthorityChangedWhileReadingBodyCannotCompleteReviewOrDecision(bool deciding, bool changeProfile)
    {
        var session = Session();
        var server = new ReviewServer();
        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [new ConsoleEnvironmentProfile { Id = "live", ServerBaseUri = Api },
             new ConsoleEnvironmentProfile { Id = "other", ServerBaseUri = Api }], activeProfileId: "live");
        var client = Client(session, server, profiles);
        var review = deciding ? (await client.OpenSubmitReviewAsync("deploy-1")).Value : null;
        server.BeforeBody = () =>
        {
            if (changeProfile) profiles.ActivateProfileAsync("other").GetAwaiter().GetResult();
            else session.Set(Principal("another-human"), new ConsoleSessionCredentials(CsrfToken: "other-csrf"));
        };
        var allowed = deciding
            ? (await client.SubmitAsync(review!, null)).IsAllowed
            : (await client.OpenSubmitReviewAsync("deploy-1")).IsAllowed;
        Assert.False(allowed);
        Assert.Equal(deciding ? 2 : 1, server.Requests.Count);
    }

    // Delay until deserialization starts, after the transport has checked response headers.
    private sealed class DelayedContent(string json, Action beforeBody) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            beforeBody();
            return stream.WriteAsync(Encoding.UTF8.GetBytes(json)).AsTask();
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    private sealed record Seen(string Line, string? Capability, string? Csrf, string? Authorization, string? ApiKey);

    private sealed class ReviewServer : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];
        public int LifetimeMinutes { get; init; } = 5;
        public Action? BeforeBody { get; set; }
        public HttpStatusCode ReviewStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode DecisionStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
            Requests.Add(new($"{request.Method} {request.RequestUri!.AbsolutePath}", Header("X-Honua-Approval-Capability"),
                Header("X-Honua-CSRF"), Header("Authorization"), Header("X-API-Key")));
            var reading = request.Method == HttpMethod.Get;
            var reviewing = reading && request.RequestUri.AbsolutePath.EndsWith("/approval-review/submit", StringComparison.Ordinal);
            var operation = new
            {
                operationId = "deploy-1",
                kind = "Deploy",
                status = reading ? "AwaitingApproval" : "Submitted",
                target = new { desiredRevision = reviewing || !reading ? "reviewed-revision" : "original-revision" }
            };
            object body = reviewing ? new
            {
                operation,
                action = "submit",
                review = new
                {
                    tenantId = "tenant-a",
                    proposalId = "deploy-1",
                    proposalVersion = 3,
                    planHash = "plan-3",
                    capability = "issued-capability",
                    expiresAt = DateTimeOffset.UtcNow.AddMinutes(LifetimeMinutes)
                }
            } : operation;
            return Task.FromResult(new HttpResponseMessage(reviewing ? ReviewStatus : DecisionStatus)
            { Content = new DelayedContent(JsonSerializer.Serialize(body), () => BeforeBody?.Invoke()) });
        }
    }
}
