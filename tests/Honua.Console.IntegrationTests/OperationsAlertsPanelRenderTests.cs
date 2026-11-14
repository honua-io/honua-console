using Bunit;
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

    private sealed class AlertClient : IConsoleAlertInboxClient
    {
        public bool Confirm { get; set; }
        public List<string?> Cursors { get; } = [];
        public List<ConsoleAlertAcknowledgement> Commands { get; } = [];
        public ObservabilityAlertEventPageResponse Page { get; set; } = new()
        {
            Items = [new() { EventId = 42, RuleName = "A map needs attention", ServiceId = "Public maps" }]
        };

        public Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(CancellationToken cancellationToken = default, string? cursor = null)
        {
            Cursors.Add(cursor);
            return Task.FromResult(OperateSectionResult<ConsoleAlertInboxSnapshot>.Allowed(new(ConsoleAlertAuthority.Capture(new ConsoleEnvironmentProfile { Id = "profile-1" }, null), Page)));
        }

        public Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
            ConsoleAlertAcknowledgement acknowledgement, CancellationToken cancellationToken = default)
        {
            Commands.Add(acknowledgement);
            return Task.FromResult(Confirm
                ? OperateSectionResult<ObservabilityAlertEventResponse>.Allowed(Page.Items[0] with { AcknowledgedAt = DateTimeOffset.UtcNow })
                : OperateSectionResult<ObservabilityAlertEventResponse>.Denied(OperateSectionStatus.Unavailable, "Acknowledgement not confirmed."));
        }
    }
}
