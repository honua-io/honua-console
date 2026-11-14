using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed partial class ConsoleOpsFindingsClientTests
{
    [Theory]
    [InlineData(ConsoleAlertLifecycleAction.Snooze)]
    [InlineData(ConsoleAlertLifecycleAction.Resolve)]
    public async Task AlertLifecycleUsesOperatorAndConfirmsTheMatchingServerState(ConsoleAlertLifecycleAction action)
    {
        var until = ConsoleAlertLifecycleCommand.NormalizeDeadline(DateTimeOffset.UtcNow.AddHours(1)).AddTicks(7);
        var requests = new List<string>();
        var handler = new AlertLifecycleHandler(async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            var response = new ObservabilityAlertEventResponse { EventId = 42, Severity = "warning" };
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("operator-test-bearer", request.Headers.Authorization!.Parameter);
                Assert.False(request.Headers.Contains("X-API-Key"));
                Assert.Equal("same-command", Assert.Single(request.Headers.GetValues("Idempotency-Key")));
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (action == ConsoleAlertLifecycleAction.Snooze)
                {
                    Assert.Equal(ConsoleAlertLifecycleCommand.NormalizeDeadline(until), body.RootElement.GetProperty("suppressUntil").GetDateTimeOffset());
                }
                response = response with
                {
                    SuppressedUntil = action == ConsoleAlertLifecycleAction.Snooze ? until : null,
                    ResolvedAt = action == ConsoleAlertLifecycleAction.Resolve ? DateTimeOffset.UtcNow : null
                };
            }
            return JsonResponse(response, OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse);
        });
        var result = await CreateClient(handler, sessions: BearerSessions(), adminApiKey: "read-key")
            .ChangeAlertLifecycleAsync(new(42, AlertAuthority(), "same-command", action, until));
        Assert.True(result.IsAllowed);
        Assert.EndsWith("/alerts/42", requests[0], StringComparison.Ordinal);
        Assert.EndsWith(action == ConsoleAlertLifecycleAction.Snooze ? "/suppress" : "/resolve", requests[1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("CRITICAL")]
    [InlineData("")]
    public async Task AlertLifecycleNeverSnoozesCriticalOrUnknownSeverity(string severity)
    {
        var methods = new List<HttpMethod>();
        var handler = new AlertLifecycleHandler(request =>
        {
            methods.Add(request.Method);
            return Task.FromResult(JsonResponse(new ObservabilityAlertEventResponse { EventId = 42, Severity = severity },
                OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        });
        var result = await CreateClient(handler, sessions: BearerSessions()).ChangeAlertLifecycleAsync(
            new(42, AlertAuthority(), "request", ConsoleAlertLifecycleAction.Snooze, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Equal(OperateSectionStatus.Forbidden, result.Status);
        Assert.Equal(HttpMethod.Get, Assert.Single(methods));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(8)]
    public async Task AlertLifecycleRejectsSnoozeOutsideSevenDaysBeforeSending(int days)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Must not send."));
        var result = await CreateClient(handler, sessions: BearerSessions()).ChangeAlertLifecycleAsync(
            new(42, AlertAuthority(), "request", ConsoleAlertLifecycleAction.Snooze, DateTimeOffset.UtcNow.AddDays(days)));
        Assert.Equal(OperateSectionStatus.Conflict, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(ConsoleAlertLifecycleAction.Snooze)]
    [InlineData(ConsoleAlertLifecycleAction.Resolve)]
    public async Task AlertLifecycleDoesNotClaimSuccessWithoutServerConfirmation(ConsoleAlertLifecycleAction action)
    {
        var handler = new RecordingHandler(_ => JsonResponse(new ObservabilityAlertEventResponse { EventId = 42, Severity = "warning" },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        var result = await CreateClient(handler, sessions: BearerSessions()).ChangeAlertLifecycleAsync(
            new(42, AlertAuthority(), "request", action, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Equal(OperateSectionStatus.Unavailable, result.Status);
        Assert.Contains("not confirmed", result.Message);
    }

    [Fact]
    public async Task AlertLifecycleRejectsCredentialAuthorityChangesBeforeMutation()
    {
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "live", ServerBaseUri = new Uri("https://server.example"),
            Account = new() { AccountId = "operator.live" }
        };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([profile]);
        var handler = new RecordingHandler(_ => JsonResponse(new ObservabilityAlertEventResponse { EventId = 42, Severity = "warning" },
            OperateObservabilityJsonContext.Default.ObservabilityAlertEventResponse));
        var provider = new ChangingAlertBearerProvider(async () =>
            await profiles.UpsertProfileAsync(profile with { Account = new() { AccountId = "other-operator" } }));
        var client = new HttpConsoleOpsFindingsClient(new HttpClient(handler), profiles, BearerSessions(),
            operatorBearerProvider: provider);
        var result = await client.ChangeAlertLifecycleAsync(new(42, AlertAuthority(), "request", ConsoleAlertLifecycleAction.Resolve));
        Assert.Equal(OperateSectionStatus.Conflict, result.Status);
        Assert.Equal(HttpMethod.Get, Assert.Single(handler.Requests).Method);
    }

    private sealed class AlertLifecycleHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
