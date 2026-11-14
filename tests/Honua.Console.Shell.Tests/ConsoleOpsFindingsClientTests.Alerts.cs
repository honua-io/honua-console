using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed partial class ConsoleOpsFindingsClientTests
{
    private static ConsoleAlertAuthority AlertAuthority(string profileId = "live") =>
        new(profileId, new Uri("https://server.example"), "", ConsoleAccountAuthMode.AccountRbac,
            "operator.live", "", "", "", null);

    [Fact]
    public async Task AlertsPreserveEnvironmentAndPaginationWithoutClaimingCompleteCoverage()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new ObservabilityAlertEventPageResponse { NextCursor = "older-alerts" },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse));
        var result = await CreateClient(handler, adminApiKey: "read-key").ListAlertsAsync();

        Assert.True(result.IsAllowed);
        Assert.Equal("live", result.Value!.Authority.ProfileId);
        Assert.Equal("older-alerts", result.Value.Page.NextCursor);
        var request = Assert.Single(handler.Requests);
        Assert.EndsWith("/observability/alerts?pageSize=50", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlertsEncodeTheContinuationCursor()
    {
        var handler = new RecordingHandler(_ => JsonResponse(new ObservabilityAlertEventPageResponse(),
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse));
        Assert.True((await CreateClient(handler).ListAlertsAsync(cursor: "opaque+/=token")).IsAllowed);
        Assert.EndsWith("?pageSize=50&cursor=opaque%2B%2F%3Dtoken",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlertsRejectCursorFromAnotherAuthorityWithoutSending()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var result = await CreateClient(handler).ListAlertsAsync(cursor: "old-cursor",
            expectedAuthority: AlertAuthority("previous-environment"));
        Assert.Equal(OperateSectionStatus.Conflict, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlertsRejectResponsesAfterEnvironmentChanges(bool acknowledge)
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live", ServerBaseUri = new Uri("https://server.example"),
            Account = new() { AccountId = "operator.live" }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
        var handler = new ChangingAlertHandler(async () =>
        {
            await profiles.UpsertProfileAsync(profile with { ServerBaseUri = new Uri("https://other.example") });
            return acknowledge
                ? JsonResponse(new ObservabilityAlertEventResponse { EventId = 42, AcknowledgedAt = DateTimeOffset.UtcNow },
                    OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse)
                : JsonResponse(new ObservabilityAlertEventPageResponse(),
                    OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse);
        });
        var client = new HttpConsoleOpsFindingsClient(new HttpClient(handler), profiles, BearerSessions());
        var status = acknowledge
            ? (await client.AcknowledgeAlertAsync(new(42, AlertAuthority(), "request-1"))).Status
            : (await client.ListAlertsAsync()).Status;
        Assert.Equal(OperateSectionStatus.Conflict, status);
    }

    [Fact]
    public async Task AlertAcknowledgementRejectsAccountChangeDuringCredentialResolution()
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live", ServerBaseUri = new Uri("https://server.example"),
            Account = new() { AccountId = "operator.live" }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var provider = new ChangingAlertBearerProvider(async () =>
            await profiles.UpsertProfileAsync(profile with { Account = new() { AccountId = "other-operator" } }));
        var client = new HttpConsoleOpsFindingsClient(new HttpClient(handler), profiles, BearerSessions(),
            operatorBearerProvider: provider);

        var result = await client.AcknowledgeAlertAsync(new(42, AlertAuthority(), "request-1"));

        Assert.Equal(OperateSectionStatus.Conflict, result.Status);
        Assert.Empty(handler.Requests);
    }

    private sealed class ChangingAlertBearerProvider(Func<Task> change) : IConsoleOperatorBearerProvider
    {
        public async Task<ConsoleOperatorBearerResolution> ResolveAsync(
            ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default)
        {
            await change();
            return new() { AccessToken = "other-operator-bearer", HasInteractiveSession = true };
        }
    }

    [Fact]
    public async Task AlertAcknowledgementUsesOperatorAndStableRetryIdentity()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new ObservabilityAlertEventResponse { EventId = 42, AcknowledgedAt = DateTimeOffset.UtcNow },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        var client = CreateClient(handler, adminApiKey: "shared-key", sessions: BearerSessions());
        var command = new ConsoleAlertAcknowledgement(42, AlertAuthority(), "same-logical-action");

        Assert.True((await client.AcknowledgeAlertAsync(command)).IsAllowed);
        Assert.True((await client.AcknowledgeAlertAsync(command)).IsAllowed);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/alerts/42/acknowledge", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("operator-test-bearer", request.Headers.Authorization!.Parameter);
            Assert.False(request.Headers.Contains("X-API-Key"));
            Assert.Equal("same-logical-action", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
        });
    }

    [Theory]
    [InlineData("live")]
    [InlineData("different-environment")]
    public async Task AlertAcknowledgementFailsClosedWithoutMatchingEnvironmentAndHumanIdentity(string profileId)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var result = await CreateClient(handler, adminApiKey: "shared-key")
            .AcknowledgeAlertAsync(new(42, AlertAuthority(profileId), "request-1"));

        Assert.False(result.IsAllowed);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("account")]
    [InlineData("tenant")]
    [InlineData("session")]
    public async Task AlertAcknowledgementRejectsChangedAuthorityWithinTheSameProfile(string change)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var authority = AlertAuthority();
        authority = change switch
        {
            "server" => authority with { ServerBaseUri = new Uri("https://other.example") },
            "account" => authority with { AccountId = "someone-else" },
            "tenant" => authority with { TenantId = "other-tenant" },
            _ => authority with { SessionAccountId = "previous-operator" }
        };
        var result = await CreateClient(handler, sessions: BearerSessions())
            .AcknowledgeAlertAsync(new(42, authority, "request-1"));
        Assert.Equal(OperateSectionStatus.Conflict, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AlertAcknowledgementReturnsUnconfirmedWhenResponseBodyFailsOrStalls(bool stall)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new IncompleteAlertContent(stall)
        });
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live", ServerBaseUri = new Uri("https://server.example"),
            Account = new() { AccountId = "operator.live" }
        };
        var client = new HttpConsoleOpsFindingsClient(
            new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) },
            new InMemoryConsoleEnvironmentProfileStore([profile]), BearerSessions());
        var result = await client.AcknowledgeAlertAsync(new(42, AlertAuthority(), "request-1"));
        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Contains("not be confirmed", result.Message, StringComparison.Ordinal);
    }

    private sealed class ChangingAlertHandler(Func<Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond();
    }

    private sealed class IncompleteAlertContent(bool stall) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new IOException("Response stream ended unexpectedly.");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            stall ? Task.Delay(Timeout.Infinite, cancellationToken)
                : throw new IOException("Response stream ended unexpectedly.");

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Theory]
    [InlineData(41, true)]
    [InlineData(42, false)]
    public async Task AlertAcknowledgementRequiresMatchingConfirmedServerResponse(long responseId, bool acknowledged)
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new ObservabilityAlertEventResponse
            {
                EventId = responseId,
                AcknowledgedAt = acknowledged ? DateTimeOffset.UtcNow : null
            }, OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        var result = await CreateClient(handler, sessions: BearerSessions())
            .AcknowledgeAlertAsync(new(42, AlertAuthority(), "request-1"));

        Assert.False(result.IsAllowed);
        Assert.Contains("not confirmed", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, OperateSectionStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, OperateSectionStatus.Missing)]
    [InlineData(HttpStatusCode.Conflict, OperateSectionStatus.Conflict)]
    public async Task AlertAcknowledgementPreservesServerDenials(HttpStatusCode status, OperateSectionStatus expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status)
        {
            Content = new IncompleteAlertContent(stall: true)
        });
        var result = await CreateClient(handler, sessions: BearerSessions())
            .AcknowledgeAlertAsync(new(42, AlertAuthority(), "request-1"));

        Assert.Equal(expected, result.Status);
    }
}
