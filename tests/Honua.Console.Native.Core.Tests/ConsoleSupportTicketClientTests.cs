using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleSupportTicketClientTests
{
    private static readonly Uri SupportBaseUri = new("https://support.honua.example");

    [Fact]
    public async Task CreateTicketPostsToTicketsRouteWithBearerToken()
    {
        var handler = new RecordingHandler(_ => JsonResponse(SampleTicket("ticket-1", "intake")));
        var client = CreateClient(handler);

        var result = await client.CreateTicketAsync(new CreateSupportTicketRequest
        {
            Severity = "sev2",
            Environment = "production",
            Symptoms = "elevated 500s",
            RequestedAction = "diagnose"
        });

        Assert.True(result.IsAllowed);
        Assert.Equal("ticket-1", result.Value!.Id);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/tickets", request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("live-token", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GetTicketReadsByIdRoute()
    {
        var handler = new RecordingHandler(_ => JsonResponse(SampleTicket("ticket-42", "diagnosed")));
        var client = CreateClient(handler);

        var result = await client.GetTicketAsync("ticket-42");

        Assert.True(result.IsAllowed);
        Assert.Equal("diagnosed", result.Value!.Phase);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/v1/tickets/ticket-42", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task NonSuccessStatusMapsToNeutralSectionStatus()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var result = await client.GetTicketAsync("missing");

        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Missing, result.Status);
    }

    [Fact]
    public async Task UnsupportedClientRendersUnsupportedWithoutCall()
    {
        var client = new UnsupportedSupportTicketClient();

        var create = await client.CreateTicketAsync(new CreateSupportTicketRequest());

        Assert.False(create.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unsupported, create.Status);
    }

    [Fact]
    public async Task ContextBuilderCapturesSessionEnvAndRecentErrors()
    {
        var profile = SampleProfile();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessions = new InMemoryConsoleAccountSessionStore();
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = "operator.live",
            DisplayName = "Live Operator",
            TenantId = "honua-prod",
            RoleIds = ["support.read", "operate.read"],
            AccessToken = "live-token"
        });

        var observability = new StubObservabilityClient(
        [
            new OperateRecentError(DateTimeOffset.Parse("2026-06-05T12:00:00Z"), "corr-1", "/api/v1/featureserver", 500, "boom")
        ]);
        var builder = new ConsoleSupportContextBuilder(profiles, sessions, observability);

        var context = await builder.CaptureAsync("/support");

        Assert.Equal("operator.live", context.UserId);
        Assert.Equal("Live Operator", context.UserDisplayName);
        Assert.Equal("honua-prod", context.TenantId);
        Assert.Contains("support.read", context.Roles);
        Assert.Equal("production", context.EnvironmentKind);
        Assert.Equal("/support", context.CurrentRoute);
        Assert.Single(context.RecentErrors);

        var rendered = context.ToSymptomContext();
        Assert.Contains("Live Operator", rendered);
        Assert.Contains("HTTP 500 /api/v1/featureserver", rendered);
    }

    [Fact]
    public void ToContextRequestProjectsStructuredSupportContextV1()
    {
        var context = new ConsoleSupportContext
        {
            UserId = "operator.live",
            UserDisplayName = "Live Operator",
            TenantId = "honua-prod",
            EnvironmentKind = "production",
            ServerUri = "https://server.example/",
            AppVersion = "2.5.0",
            Commit = "abc1234",
            CurrentRoute = "/support",
            RecentErrors =
            [
                new OperateRecentError(DateTimeOffset.Parse("2026-06-05T12:00:00Z"), "corr-1", "/api/v1/featureserver", 500, "boom")
            ]
        };

        var request = context.ToContextRequest("scoped-telemetry-key");

        Assert.Equal("1.0", request.SchemaVersion);
        Assert.Equal("operator.live", request.User!.Id);
        Assert.Equal("Live Operator", request.User.DisplayName);
        Assert.Equal("honua-prod", request.Tenant!.Id);
        Assert.Equal("production", request.EnvKind);
        Assert.Equal("2.5.0", request.AppVersion);
        Assert.Equal("abc1234", request.Commit);
        Assert.Equal("/support", request.Route);
        Assert.Equal("https://server.example/", request.InstanceUrl);
        Assert.Equal("scoped-telemetry-key", request.ScopedKey);

        var error = Assert.Single(request.RecentErrors!);
        Assert.Equal("/api/v1/featureserver", error.Path);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal("corr-1", error.CorrelationId);
        Assert.Equal("boom", error.Message);
        Assert.Equal(DateTimeOffset.Parse("2026-06-05T12:00:00Z"), error.Timestamp);
    }

    [Fact]
    public void ToContextRequestCollapsesEmptyOptionalsToNullAndOmitsScopedKey()
    {
        var context = new ConsoleSupportContext
        {
            EnvironmentKind = string.Empty,
            CurrentRoute = "/support"
        };

        var request = context.ToContextRequest(scopedKey: null);

        Assert.Equal("1.0", request.SchemaVersion);
        Assert.Null(request.User);
        Assert.Null(request.Tenant);
        Assert.Null(request.EnvKind);
        Assert.Null(request.AppVersion);
        Assert.Null(request.Commit);
        Assert.Null(request.InstanceUrl);
        Assert.Null(request.RecentErrors);
        Assert.Null(request.ScopedKey);
        Assert.Equal("/support", request.Route);
    }

    [Fact]
    public void CreateRequestSerializesContextBlockAndNeverLeaksScopedKeyIntoSymptoms()
    {
        var context = new ConsoleSupportContext
        {
            UserId = "operator.live",
            TenantId = "honua-prod",
            EnvironmentKind = "production",
            ServerUri = "https://server.example/",
            CurrentRoute = "/support"
        };

        var request = new CreateSupportTicketRequest
        {
            Severity = "sev2",
            Environment = "production",
            Symptoms = "elevated 500s",
            RequestedAction = "diagnose",
            InstanceUrl = context.ServerUri,
            Context = context.ToContextRequest("super-secret-key")
        };

        var json = JsonSerializer.Serialize(request, SupportTicketJsonContext.Default.CreateSupportTicketRequest);

        // Secret rides only in the structured context block, never in symptoms.
        Assert.Contains("\"scopedKey\":\"super-secret-key\"", json);
        Assert.DoesNotContain("super-secret-key", request.Symptoms);
        Assert.Contains("\"schemaVersion\":\"1.0\"", json);
        Assert.Contains("\"context\":", json);
    }

    private static HttpSupportTicketClient CreateClient(HttpMessageHandler handler)
    {
        var profile = SampleProfile();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile], activeProfileId: profile.Id);
        var sessions = new InMemoryConsoleAccountSessionStore();
        sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = profile.Id,
            AccountId = "operator.live",
            AccessToken = "live-token"
        }).GetAwaiter().GetResult();

        return new HttpSupportTicketClient(new HttpClient(handler), SupportBaseUri, profiles, sessions);
    }

    private static ConsoleEnvironmentProfile SampleProfile() =>
        new()
        {
            Id = "live",
            DisplayName = "Live Server Alpha",
            ServerBaseUri = new Uri("https://server.example"),
            EnvironmentKind = "production",
            TenantId = "honua-prod",
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = "operator.live",
                DisplayName = "Live Operator"
            }
        };

    private static SupportTicketResponse SampleTicket(string id, string phase) =>
        new()
        {
            Id = id,
            CustomerId = "honua-prod",
            Severity = "sev2",
            Environment = "production",
            Service = "honua-server",
            Symptoms = "elevated 500s",
            RequestedAction = "diagnose",
            AllowedAccessMode = "read-only",
            Phase = phase,
            CustomerStatus = "received",
            CreatedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-05T12:01:00Z")
        };

    private static HttpResponseMessage JsonResponse(SupportTicketResponse value)
    {
        var json = JsonSerializer.Serialize(value, SupportTicketJsonContext.Default.SupportTicketResponse);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Headers.Authorization is { } auth)
            {
                clone.Headers.Authorization = new AuthenticationHeaderValue(auth.Scheme, auth.Parameter);
            }

            Requests.Add(clone);
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubObservabilityClient(IReadOnlyList<OperateRecentError> recentErrors) : IConsoleOperateObservabilityClient
    {
        public Task<OperateSectionResult<OperateFleetOverview>> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateFleetOverview>.Allowed(OperateFleetOverview.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateEventRow>>> QueryEventsAsync(OperateEventQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateEventRow>>.Allowed([]));

        public Task<OperateSectionResult<OperateLogsView>> GetLogsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateLogsView>.Allowed(OperateLogsView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateAlertRecord>>> GetAlertsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateAlertRecord>>.Allowed([]));

        public Task<OperateSectionResult<OperateRulesView>> GetRulesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateRulesView>.Allowed(OperateRulesView.Empty));

        public Task<OperateSectionResult<IReadOnlyList<OperateJobRun>>> GetJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateJobRun>>.Allowed([]));

        public Task<OperateSectionResult<OperateJobRun>> GetJobDetailAsync(string jobRunId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<OperateJobRun>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<IReadOnlyList<OperateInvestigation>>> GetInvestigationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateInvestigation>>.Allowed([]));

        public Task<OperateSectionResult<IReadOnlyList<OperateRecentError>>> GetRecentErrorsAsync(int limit = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<OperateRecentError>>.Allowed(recentErrors));
    }
}
