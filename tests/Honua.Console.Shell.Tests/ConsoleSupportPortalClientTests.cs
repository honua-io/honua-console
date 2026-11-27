using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleSupportPortalClientTests
{
    private static readonly Uri BffBaseUri = new("https://console.honua.example/");

    [Fact]
    public async Task PreflightPostsToBffWithoutClientAuthHeader()
    {
        var expected = new SupportPreflightResponse
        {
            ProductArea = "server",
            IssueKind = "bug",
            ClassificationProvenance = "inferred",
            RedactionPreview = new SupportPreflightRedactionPreviewResponse
            {
                HeadersDropped = 2,
                BodiesRedacted = 1,
                ScopedKeyWillBeBrokered = false
            }
        };
        var handler = new RecordingHandler(_ => JsonResponse(expected, SupportPortalJsonContext.Default.SupportPreflightResponse));
        var client = new HttpConsoleSupportPortalClient(new HttpClient(handler), BffBaseUri);

        var result = await client.RunPreflightAsync(new SupportPreflightRequest
        {
            Severity = "sev2",
            Service = "honua-server",
            Symptoms = "FeatureServer returning 500"
        });

        Assert.True(result.IsAllowed);
        Assert.Equal("server", result.Value!.ProductArea);
        Assert.Equal(2, result.Value.RedactionPreview!.HeadersDropped);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/support/preflight", request.RequestUri!.AbsolutePath);
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public async Task Unconfigured503MapsToUnsupportedWithSetupGuidance()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"error":"support_portal_unconfigured","message":"Configure HonuaSupport:BaseUrl and HonuaSupport:ApiKey."}""",
                Encoding.UTF8,
                "application/json")
        });
        var client = new HttpConsoleSupportPortalClient(new HttpClient(handler), BffBaseUri);

        var result = await client.ListAdvisoriesAsync();

        Assert.False(result.IsAllowed);
        Assert.Equal(OperateSectionStatus.Unsupported, result.Status);
        Assert.Contains("HonuaSupport:BaseUrl", result.Message);
    }

    [Fact]
    public async Task AdvisoriesFleetTicketsApproveAndRevokeUseExpectedBffRoutes()
    {
        var handler = new RecordingHandler(RespondByPath);
        var client = new HttpConsoleSupportPortalClient(new HttpClient(handler), BffBaseUri);

        var advisories = await client.ListAdvisoriesAsync();
        var fleet = await client.ListFleetInstancesAsync();
        var tickets = await client.ListTicketsAsync();
        var approved = await client.ApproveTicketAsync("ticket-7", new SupportApproveTicketRequest { AccessMode = "operator-scoped", TtlMinutes = 30 });
        var revoked = await client.RevokeAccessAsync("ticket-7", "done");

        Assert.True(advisories.IsAllowed);
        Assert.Single(advisories.Value!.Advisories);
        Assert.True(fleet.IsAllowed);
        Assert.Single(fleet.Value!.Instances);
        Assert.True(tickets.IsAllowed);
        Assert.Single(tickets.Value!);
        Assert.True(approved.IsAllowed);
        Assert.Equal("ticket-7", approved.Value!.Id);
        Assert.True(revoked.IsAllowed);
        Assert.True(revoked.Value!.ScopedCredentialRevoked);

        Assert.Equal(
            [
                "/api/support/security/advisories",
                "/api/support/fleet/instances",
                "/api/support/tickets",
                "/api/support/tickets/ticket-7/approve",
                "/api/support/tickets/ticket-7/revoke-access"
            ],
            handler.Requests.Select(r => r.RequestUri!.AbsolutePath).ToArray());
    }

    [Fact]
    public async Task UnsupportedClientReturnsUnsupportedWithoutNetwork()
    {
        var client = new UnsupportedConsoleSupportPortalClient();

        var advisories = await client.ListAdvisoriesAsync();
        var fleet = await client.ListFleetInstancesAsync();
        var tickets = await client.ListTicketsAsync();
        var preflight = await client.RunPreflightAsync(new SupportPreflightRequest());

        Assert.Equal(OperateSectionStatus.Unsupported, advisories.Status);
        Assert.Equal(OperateSectionStatus.Unsupported, fleet.Status);
        Assert.Equal(OperateSectionStatus.Unsupported, tickets.Status);
        Assert.Equal(OperateSectionStatus.Unsupported, preflight.Status);
        Assert.Contains("HonuaSupport:BaseUrl", advisories.Message);
    }

    private static HttpResponseMessage RespondByPath(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath switch
        {
            "/api/support/security/advisories" => JsonResponse(
                SampleAdvisoriesResponse(),
                SupportPortalJsonContext.Default.SupportSecurityAdvisoriesResponse),
            "/api/support/fleet/instances" => JsonResponse(
                SampleFleetResponse(),
                SupportPortalJsonContext.Default.SupportFleetInstancesResponse),
            "/api/support/tickets" => JsonResponse(
                (IReadOnlyList<SupportTicketResponse>)[SampleTicket("ticket-7")],
                SupportPortalJsonContext.Default.IReadOnlyListSupportTicketResponse),
            "/api/support/tickets/ticket-7/approve" => JsonResponse(
                SampleTicket("ticket-7"),
                SupportPortalJsonContext.Default.SupportTicketResponse),
            "/api/support/tickets/ticket-7/revoke-access" => JsonResponse(
                SampleRevokeResponse("ticket-7"),
                SupportPortalJsonContext.Default.SupportRevokeAccessResponse),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

    private static SupportSecurityAdvisoriesResponse SampleAdvisoriesResponse() => new()
    {
        Count = 1,
        Advisories =
        [
            new SupportSecurityAdvisoryItem
            {
                AdvisoryId = "HONUA-SA-2026-001",
                CveId = "CVE-2026-10001",
                Title = "TLS token validation patch",
                Severity = "high",
                CisaKev = true,
                Component = "honua-server",
                VexStatus = "affected",
                CustomerStatus = "affected",
                FixedVersion = "1.4.2",
                UpdatedAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z")
            }
        ]
    };

    private static SupportFleetInstancesResponse SampleFleetResponse() => new()
    {
        Count = 1,
        Instances =
        [
            new SupportFleetInstanceItem
            {
                CustomerId = "cust-1",
                InstanceId = "prod-west-1",
                Component = "honua-server",
                Version = "1.4.1",
                EnvKind = "production",
                LastSource = "telemetry",
                FirstSeenAt = DateTimeOffset.Parse("2026-11-20T00:00:00Z"),
                LastSeenAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z")
            }
        ]
    };

    private static SupportRevokeAccessResponse SampleRevokeResponse(string ticketId) => new()
    {
        TicketId = ticketId,
        ScopedCredentialRevoked = true,
        AwsGrantRevoked = false,
        Ticket = SampleTicket(ticketId)
    };

    private static SupportTicketResponse SampleTicket(string id) => new()
    {
        Id = id,
        CustomerId = "cust-1",
        Severity = "sev2",
        Environment = "production",
        Service = "honua-server",
        Symptoms = "500s",
        RequestedAction = "diagnose",
        AllowedAccessMode = "operator-scoped",
        TtlMinutes = 30,
        Phase = "triaging",
        CustomerStatus = "in-progress",
        CreatedAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2026-11-27T00:05:00Z")
    };

    private static HttpResponseMessage JsonResponse<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(value, typeInfo), Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
