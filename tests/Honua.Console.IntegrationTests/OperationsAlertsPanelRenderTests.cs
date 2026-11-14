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
            Items = [new() { EventId = 42, RuleName = "A map needs attention", ServiceId = "Public maps" }]
        };

        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null)
        {
            Cursors.Add(cursor);
            if (cursor is not null) { Assert.Equal("profile-1", expectedAuthority?.ProfileId); }
            if (PendingRead is { } pending) { return pending; }
            return Task.FromResult(OperateSectionResult<ConsoleAlertInboxSnapshot>.Allowed(new(ConsoleAlertAuthority.Capture(new ConsoleEnvironmentProfile { Id = "profile-1" }, null), Page)));
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
