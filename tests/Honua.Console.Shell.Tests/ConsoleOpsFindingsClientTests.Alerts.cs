using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed partial class ConsoleOpsFindingsClientTests
{
    [Fact]
    public async Task AlertsPreserveEnvironmentAndPaginationWithoutClaimingCompleteCoverage()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new ObservabilityAlertEventPageResponse { NextCursor = "older-alerts" },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventPageResponse));
        var result = await CreateClient(handler, adminApiKey: "read-key").ListAlertsAsync();

        Assert.True(result.IsAllowed);
        Assert.Equal("live", result.Value!.ProfileId);
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
    public async Task AlertAcknowledgementUsesOperatorAndStableRetryIdentity()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            new ObservabilityAlertEventResponse { EventId = 42, AcknowledgedAt = DateTimeOffset.UtcNow },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        var client = CreateClient(handler, adminApiKey: "shared-key", sessions: BearerSessions());
        var command = new ConsoleAlertAcknowledgement(42, "live", "same-logical-action");

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
            .AcknowledgeAlertAsync(new(42, profileId, "request-1"));

        Assert.False(result.IsAllowed);
        Assert.Empty(handler.Requests);
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
            .AcknowledgeAlertAsync(new(42, "live", "request-1"));

        Assert.False(result.IsAllowed);
        Assert.Contains("not confirmed", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, OperateSectionStatus.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, OperateSectionStatus.Missing)]
    [InlineData(HttpStatusCode.Conflict, OperateSectionStatus.Conflict)]
    public async Task AlertAcknowledgementPreservesServerDenials(HttpStatusCode status, OperateSectionStatus expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var result = await CreateClient(handler, sessions: BearerSessions())
            .AcknowledgeAlertAsync(new(42, "live", "request-1"));

        Assert.Equal(expected, result.Status);
    }
}
