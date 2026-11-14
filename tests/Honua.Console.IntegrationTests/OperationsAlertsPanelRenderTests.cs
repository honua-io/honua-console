using Bunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperationsAlertsPanelRenderTests
{
    [Fact]
    public void AcknowledgementWaitsForConfirmationAndRetainsIdentityOnRetry()
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledge-alert]")));
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Contains("not confirmed", panel.Find("[data-alert-action-message]").TextContent));
        Assert.Empty(panel.FindAll("[data-acknowledged]"));
        client.Confirm = true;
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledged]")));
        Assert.Empty(panel.FindAll("[data-acknowledge-alert]"));
        Assert.Equal(2, client.Commands.Count);
        Assert.Equal(client.Commands[0], client.Commands[1]);
        Assert.Equal("profile-1", client.Commands[0].Authority.ProfileId);
        Assert.Contains("underlying problem may still need attention", panel.Markup, StringComparison.Ordinal);
        Assert.False(panel.Find("details").HasAttribute("open"));
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledge-alert]")));
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Equal(3, client.Commands.Count));
        Assert.NotEqual(client.Commands[1].RequestId, client.Commands[2].RequestId);
    }

    [Fact]
    public void RefreshConfirmationRetiresAnUncertainCommandBeforeAnotherLifecycle()
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(client.Commands));
        var original = client.Page;
        client.Page = original with { Items = [original.Items[0] with { AcknowledgedAt = DateTimeOffset.UtcNow }] };
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledged]")));
        client.Page = original;
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledge-alert]")));
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Equal(2, client.Commands.Count));
        Assert.NotEqual(client.Commands[0].RequestId, client.Commands[1].RequestId);
    }

    [Fact]
    public void EmptyAndPartialAlertPagesDoNotAssertOverallHealthOrCompleteCounts()
    {
        using var context = new BunitContext();
        var client = new AlertClient { Page = new() { NextCursor = "more" } };
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.WaitForAssertion(() => Assert.Contains("does not confirm", panel.Find("[data-no-alerts]").TextContent));
        Assert.Contains("does not include all alerts", panel.Markup, StringComparison.Ordinal);
        panel.Find("[data-next-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Equal("more", client.Cursors.Last()));
        Assert.DoesNotContain("Everything is working", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingConnectionDoesNotRenderAnEmptySuccessState()
    {
        using var context = new BunitContext();
        var panel = context.Render<OperationsAlertsPanel>();
        panel.WaitForAssertion(() => Assert.Contains("unavailable", panel.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(panel.FindAll("[data-no-alerts]"));
    }

    [Fact]
    public async Task OperatorChangeDiscardsDelayedReadAndClearsPagination()
    {
        using var context = new BunitContext();
        var authentication = new ChangingAuthentication();
        var client = new AlertClient();
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        var delayed = new TaskCompletionSource<OperateSectionResult<ConsoleAlertInboxSnapshot>>();
        client.PendingRead = delayed.Task;
        panel.Find("[data-refresh-alerts]").Click();
        client.PendingRead = null;
        client.Page = new();
        await panel.InvokeAsync(authentication.Change);
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-no-alerts]")));
        delayed.SetResult(OperateSectionResult<ConsoleAlertInboxSnapshot>.Allowed(new(
            ConsoleAlertAuthority.Capture(new ConsoleEnvironmentProfile { Id = "old" }, null),
            new() { Items = [new() { EventId = 99, RuleName = "Old operator alert" }], NextCursor = "old-cursor" })));
        await panel.InvokeAsync(() => Task.CompletedTask);
        Assert.DoesNotContain("Old operator alert", panel.Markup);
        Assert.Empty(panel.FindAll("[data-next-alerts]"));
    }

    [Fact]
    public async Task OperatorChangeDiscardsDelayedAcknowledgement()
    {
        using var context = new BunitContext();
        var authentication = new ChangingAuthentication();
        var client = new AlertClient();
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        var delayed = new TaskCompletionSource<OperateSectionResult<ObservabilityAlertEventResponse>>();
        client.PendingAcknowledgement = delayed.Task;
        panel.Find("[data-acknowledge-alert]").Click();
        client.Page = new();
        await panel.InvokeAsync(authentication.Change);
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-no-alerts]")));
        delayed.SetResult(OperateSectionResult<ObservabilityAlertEventResponse>.Allowed(
            new() { EventId = 42, AcknowledgedAt = DateTimeOffset.UtcNow }));
        await panel.InvokeAsync(() => Task.CompletedTask);
        Assert.Empty(panel.FindAll("[data-acknowledged]"));
        Assert.Empty(panel.FindAll("[data-alert-action-message]"));
    }

    [Fact]
    public void SnoozeRetriesTheSameCommandAndResolveRequiresServerConfirmation()
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Contains("not confirmed", panel.Find("[data-alert-action-message]").TextContent));
        Assert.Empty(panel.FindAll("[data-snoozed]"));
        client.Confirm = true;
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        Assert.Equal(client.LifecycleCommands[0], client.LifecycleCommands[1]);
        panel.Find("[data-resolve-alert]").Click();
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-resolve-alert]")));
        Assert.Contains("does not confirm", panel.Find("[data-alert-action-message]").TextContent);
        Assert.NotEqual(client.LifecycleCommands[1].RequestId, client.LifecycleCommands[2].RequestId);
    }

    [Fact]
    public void ExpiredUncertainSnoozeStartsANewHourWithoutRefresh()
    {
        using var context = new BunitContext();
        var clock = new AlertClock();
        var client = new AlertClient();
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(client.LifecycleCommands));
        var expired = client.LifecycleCommands[0];
        clock.Now = expired.SuppressUntil!.Value.AddSeconds(1);
        client.Confirm = true;
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        Assert.Equal(2, client.LifecycleCommands.Count);
        var renewed = client.LifecycleCommands[1];
        Assert.NotEqual(expired.RequestId, renewed.RequestId);
        Assert.Equal(ConsoleAlertLifecycleCommand.NormalizeDeadline(clock.Now.AddHours(1)), renewed.SuppressUntil);
        Assert.Equal(expired.Authority, renewed.Authority);
    }

    private sealed class AlertClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedSnoozeRetiresAnUncertainResolveIdentity(bool loseSnoozeResponse)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        var stored = client.Page.Items[0];
        var consumed = new HashSet<string>();
        client.MutateLifecycle = command =>
        {
            if (consumed.Add(command.RequestId))
            {
                stored = stored with
                {
                    SuppressedUntil = command.SuppressUntil,
                    ResolvedAt = command.Action == ConsoleAlertLifecycleAction.Resolve ? DateTimeOffset.UtcNow : null
                };
                client.Page = client.Page with { Items = [stored] };
                if (consumed.Count == 1 || (loseSnoozeResponse && consumed.Count == 2))
                {
                    return OperateSectionResult<ObservabilityAlertEventResponse>.Denied(
                        OperateSectionStatus.Unavailable, "Response lost after commit.");
                }
            }
            return OperateSectionResult<ObservabilityAlertEventResponse>.Allowed(stored);
        };
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-resolve-alert]").Click();
        panel.WaitForAssertion(() => Assert.Contains("Response lost", panel.Markup));
        panel.Find("[data-snooze-alert]").Click();
        if (loseSnoozeResponse)
        {
            panel.WaitForAssertion(() => Assert.Equal(2, client.LifecycleCommands.Count));
            panel.Find("[data-refresh-alerts]").Click();
        }
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        Assert.Equal(0, client.LifecycleCommands[1].SuppressUntil!.Value.Ticks % 10);
        panel.Find("[data-resolve-alert]").Click();
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-resolve-alert]")));
        Assert.Equal(3, consumed.Count);
        Assert.NotEqual(client.LifecycleCommands[0].RequestId, client.LifecycleCommands[2].RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedSnoozeRetiresAnUncertainAcknowledgementIdentity(bool loseSnoozeResponse)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(client.Commands));
        client.Confirm = !loseSnoozeResponse;
        panel.Find("[data-snooze-alert]").Click();
        if (loseSnoozeResponse)
        {
            panel.WaitForAssertion(() => Assert.Single(client.LifecycleCommands));
            client.Page = client.Page with
            {
                Items = [client.Page.Items[0] with
                {
                    SuppressedUntil = client.LifecycleCommands[0].SuppressUntil
                }]
            };
            panel.Find("[data-refresh-alerts]").Click();
        }
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        client.Confirm = true;
        panel.Find("[data-acknowledge-alert]").Click();
        panel.WaitForAssertion(() => Assert.Equal(2, client.Commands.Count));
        Assert.NotEqual(client.Commands[0].RequestId, client.Commands[1].RequestId);
    }

    [Theory]
    [InlineData(ConsoleAlertLifecycleAction.Snooze, false)]
    [InlineData(ConsoleAlertLifecycleAction.Snooze, true)]
    [InlineData(ConsoleAlertLifecycleAction.Resolve, false)]
    [InlineData(ConsoleAlertLifecycleAction.Resolve, true)]
    public void ConfirmedAcknowledgementRetiresUncertainLifecycleIdentities(
        ConsoleAlertLifecycleAction action, bool loseAcknowledgementResponse)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        var selector = action == ConsoleAlertLifecycleAction.Snooze ? "[data-snooze-alert]" : "[data-resolve-alert]";
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Single(client.LifecycleCommands));
        client.Confirm = !loseAcknowledgementResponse;
        panel.Find("[data-acknowledge-alert]").Click();
        if (loseAcknowledgementResponse)
        {
            panel.WaitForAssertion(() => Assert.Single(client.Commands));
            client.Page = client.Page with { Items = [client.Page.Items[0] with { AcknowledgedAt = DateTimeOffset.UtcNow }] };
            panel.Find("[data-refresh-alerts]").Click();
        }
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-acknowledged]")));
        client.Confirm = true;
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Equal(2, client.LifecycleCommands.Count));
        Assert.NotEqual(client.LifecycleCommands[0].RequestId, client.LifecycleCommands[1].RequestId);
    }

    [Theory]
    [InlineData("acknowledged", "resolve")]
    [InlineData("snoozed", "resolve")]
    [InlineData("snoozed", "acknowledge")]
    public void UnchangedRefreshRetainsUncertainIdentity(string initialState, string action)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        client.Page = client.Page with
        {
            Items = [client.Page.Items[0] with
            {
                AcknowledgedAt = initialState == "acknowledged" ? DateTimeOffset.UtcNow : null,
                SuppressedUntil = initialState == "snoozed" ? DateTimeOffset.UtcNow.AddMinutes(10) : null
            }]
        };
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        var selector = $"[data-{action}-alert]";
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Contains("not confirmed", panel.Markup));
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("[data-alert-action-message]")));
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Equal(2, action == "acknowledge" ? client.Commands.Count : client.LifecycleCommands.Count));
        if (action == "acknowledge") { Assert.Equal(client.Commands[0], client.Commands[1]); }
        else { Assert.Equal(client.LifecycleCommands[0], client.LifecycleCommands[1]); }
    }

    [Theory]
    [InlineData("acknowledge")]
    [InlineData("resolve")]
    [InlineData("snooze")]
    public void ExternalSnoozeRetiresUncertainIdentities(string action)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        var selector = $"[data-{action}-alert]";
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Contains("not confirmed", panel.Markup));
        var originalId = action == "acknowledge" ? client.Commands[0].RequestId : client.LifecycleCommands[0].RequestId;
        client.Page = client.Page with { Items = [client.Page.Items[0] with { SuppressedUntil = DateTimeOffset.UtcNow.AddMinutes(10) }] };
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        if (action == "snooze")
        {
            client.Page = client.Page with { Items = [client.Page.Items[0] with { SuppressedUntil = DateTimeOffset.UtcNow.AddMinutes(-1) }] };
            panel.Find("[data-refresh-alerts]").Click();
            panel.WaitForAssertion(() => Assert.Single(panel.FindAll(selector)));
        }
        client.Confirm = true;
        panel.Find(selector).Click();
        panel.WaitForAssertion(() => Assert.Equal(2, action == "acknowledge" ? client.Commands.Count : client.LifecycleCommands.Count));
        var retryId = action == "acknowledge" ? client.Commands[1].RequestId : client.LifecycleCommands[1].RequestId;
        Assert.NotEqual(originalId, retryId);
    }

    [Fact]
    public void RefreshConfirmationRetiresAnUncertainSnoozeBeforeAnotherOccurrence()
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Single(client.LifecycleCommands));
        var original = client.Page;
        client.Page = original with { Items = [original.Items[0] with { SuppressedUntil = client.LifecycleCommands[0].SuppressUntil }] };
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snoozed]")));
        client.Page = original;
        panel.Find("[data-refresh-alerts]").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-snooze-alert]")));
        panel.Find("[data-snooze-alert]").Click();
        panel.WaitForAssertion(() => Assert.Equal(2, client.LifecycleCommands.Count));
        Assert.NotEqual(client.LifecycleCommands[0].RequestId, client.LifecycleCommands[1].RequestId);
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("CRITICAL")]
    [InlineData("")]
    public void CriticalAndUnknownSeverityAlertsCannotBeSnoozed(string severity)
    {
        using var context = new BunitContext();
        var client = new AlertClient();
        client.Page = client.Page with { Items = [client.Page.Items[0] with { Severity = severity }] };
        context.Services.AddSingleton<IConsoleAlertInboxClient>(client);
        var panel = context.Render<OperationsAlertsPanel>();
        Assert.Empty(panel.FindAll("[data-snooze-alert]"));
        Assert.Single(panel.FindAll("[data-resolve-alert]"));
    }

    private sealed class ChangingAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            CreateState();
        public void Change() => NotifyAuthenticationStateChanged(CreateState());

        private static Task<AuthenticationState> CreateState() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class AlertClient : IConsoleAlertInboxClient
    {
        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>>? PendingRead { get; set; }
        public Task<OperateSectionResult<ObservabilityAlertEventResponse>>? PendingAcknowledgement { get; set; }
        public bool Confirm { get; set; }
        public List<string?> Cursors { get; } = [];
        public List<ConsoleAlertAcknowledgement> Commands { get; } = [];
        public ObservabilityAlertEventPageResponse Page { get; set; } = new()
        {
            Items = [new() { EventId = 42, RuleName = "A map needs attention", ServiceId = "Public maps", Severity = "warning" }]
        };

        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null)
        {
            Cursors.Add(cursor);
            if (cursor is not null) { Assert.Equal("profile-1", expectedAuthority?.ProfileId); }
            if (PendingRead is { } pending) { return pending; }
            return Task.FromResult(OperateSectionResult<ConsoleAlertInboxSnapshot>.Allowed(new(ConsoleAlertAuthority.Capture(new ConsoleEnvironmentProfile { Id = "profile-1" }, null), Page)));
        }

        public List<ConsoleAlertLifecycleCommand> LifecycleCommands { get; } = [];
        public Func<ConsoleAlertLifecycleCommand, OperateSectionResult<ObservabilityAlertEventResponse>>? MutateLifecycle { get; set; }

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> ChangeAlertLifecycleAsync(
            ConsoleAlertLifecycleCommand command, CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(command);
            if (MutateLifecycle is { } mutate) { return Task.FromResult(mutate(command)); }
            return Task.FromResult(Confirm
                ? OperateSectionResult<ObservabilityAlertEventResponse>.Allowed(Page.Items[0] with
                {
                    SuppressedUntil = command.SuppressUntil,
                    ResolvedAt = command.Action == ConsoleAlertLifecycleAction.Resolve ? DateTimeOffset.UtcNow : null
                })
                : OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Unavailable, "Change not confirmed."));
        }

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
            ConsoleAlertAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        {
            Commands.Add(acknowledgement);
            if (PendingAcknowledgement is { } pending) { return pending; }
            return Task.FromResult(Confirm
                ? OperateSectionResult<ObservabilityAlertEventResponse>.Allowed(Page.Items[0] with { AcknowledgedAt = DateTimeOffset.UtcNow })
                : OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Unavailable, "Acknowledgement not confirmed."));
        }
    }
}
