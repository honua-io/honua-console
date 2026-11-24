using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal sealed class FakeActivityFeedClient : IConsoleActivityFeedClient
{
    public List<ConsoleActivityFeedQuery> Queries { get; } = [];

    public Func<ConsoleActivityFeedQuery, OperateSectionResult<ConsoleActivityFeedPage>> Respond { get; init; } =
        _ => OperateSectionResult<ConsoleActivityFeedPage>.Allowed(new(Sample, NextCursor: null));

    public static IReadOnlyList<ObservabilityAuditRecordResponse> Sample { get; } =
    [
        new()
        {
            AuditId = 3, Timestamp = new DateTimeOffset(2026, 11, 23, 9, 41, 12, TimeSpan.Zero),
            Actor = "kai@example.org", ActorType = "UserId", ActorAgent = "Claude Desktop",
            ResourceType = "operation_proposal", ResourceId = "p-47", Action = "operation.proposed", Outcome = "Success",
            CorrelationId = "corr-47",
        },
        new()
        {
            AuditId = 2, Timestamp = new DateTimeOffset(2026, 11, 23, 9, 32, 4, TimeSpan.Zero),
            Actor = "dana@example.org", ActorType = "UserId", ResourceType = "operation_proposal", ResourceId = "p-41",
            Action = "operation.approved", Outcome = "Success", CorrelationId = "corr-41",
        },
        new()
        {
            AuditId = 1, Timestamp = new DateTimeOffset(2026, 11, 23, 9, 30, 18, TimeSpan.Zero),
            Actor = "3f9c1e", ActorType = "ApiKey", ResourceType = "connection", ResourceId = "parcels",
            Action = "connection.update", Outcome = "Failure", CorrelationId = "corr-1",
        },
    ];

    public Task<OperateSectionResult<ConsoleActivityFeedPage>> ReadAsync(
        ConsoleActivityFeedQuery query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(Respond(query));
    }
}

public sealed class ConsoleActivityFeedRenderTests
{
    private static BunitContext NewContext(IConsoleActivityFeedClient? feed)
    {
        var context = new BunitContext();
        if (feed is not null) context.Services.AddSingleton(feed);
        return context;
    }

    [Fact]
    public void Feed_ListsChangesAndFiltersByActor()
    {
        var client = new FakeActivityFeedClient();
        using var context = NewContext(client);
        var feed = context.Render<ConsoleActivityFeed>();
        var rows = feed.FindAll("[data-feed-row]");
        Assert.Equal(3, rows.Count);
        Assert.Contains("Claude Desktop", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Agent · on behalf of kai@example.org", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("inbox?proposalId=p-47&source=serving", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("key", rows[2].QuerySelector("[data-feed-actor-kind]")!.GetAttribute("data-feed-actor-kind"));
        Assert.Contains("Failure", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Equal("3 changes on page 1, newest first", feed.Find("[data-feed-range]").TextContent);
        feed.Find("[data-feed-actor]").Change(nameof(ConsoleActivityActorFilter.Agent));
        Assert.Equal(ConsoleActivityActorFilter.Agent, client.Queries[^1].Actor);
        Assert.Single(feed.FindAll("[data-feed-row]"));
    }

    [Fact]
    public void Feed_PagesOlderAndBackWithTheServerCursor()
    {
        var fail = false;
        var client = new FakeActivityFeedClient
        {
            Respond = query => fail
                ? OperateSectionResult<ConsoleActivityFeedPage>.Denied(OperateSectionStatus.Unavailable, "Temporary audit failure.")
                : OperateSectionResult<ConsoleActivityFeedPage>.Allowed(
                    new(FakeActivityFeedClient.Sample, query.Cursor is null ? "next-1" : null)),
        };
        using var context = NewContext(client);
        var feed = context.Render<ConsoleActivityFeed>();
        Assert.True(feed.Find("[data-feed-newer]").HasAttribute("disabled"));
        feed.Find("[data-feed-older]").Click();
        Assert.Equal("next-1", client.Queries[^1].Cursor);
        Assert.True(feed.Find("[data-feed-older]").HasAttribute("disabled"));
        Assert.Equal("3 changes on page 2, newest first", feed.Find("[data-feed-range]").TextContent);
        fail = true;
        feed.Find("[data-feed-newer]").Click();
        Assert.Contains("Temporary audit failure.", feed.Markup, StringComparison.Ordinal);
        Assert.Equal("3 changes on page 2, newest first", feed.Find("[data-feed-range]").TextContent);
        Assert.False(feed.Find("[data-feed-newer]").HasAttribute("disabled"));
        fail = false;
        feed.Find("[data-feed-newer]").Click();
        Assert.Null(client.Queries[^1].Cursor);
        Assert.Equal("3 changes on page 1, newest first", feed.Find("[data-feed-range]").TextContent);
        Assert.DoesNotContain("Temporary audit failure.", feed.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingActorOnAnOlderPageStartsTheFilteredFeedAtPageOne()
    {
        var client = new FakeActivityFeedClient
        {
            Respond = query => OperateSectionResult<ConsoleActivityFeedPage>.Allowed(
                new(FakeActivityFeedClient.Sample, query.Cursor is null ? "next-1" : null)),
        };
        using var context = NewContext(client);
        var feed = context.Render<ConsoleActivityFeed>();
        feed.Find("[data-feed-older]").Click();
        Assert.Equal("next-1", client.Queries[^1].Cursor);

        feed.Find("[data-feed-actor]").Change(nameof(ConsoleActivityActorFilter.Agent));

        Assert.Equal(new ConsoleActivityFeedQuery(ConsoleActivityActorFilter.Agent, Cursor: null), client.Queries[^1]);
        Assert.True(feed.Find("[data-feed-newer]").HasAttribute("disabled"));
        Assert.Equal("1 change on page 1, newest first", feed.Find("[data-feed-range]").TextContent);
        Assert.Equal("3", Assert.Single(feed.FindAll("[data-feed-row]")).GetAttribute("data-feed-row"));
    }

    [Fact]
    public void Feed_ShowsTheSharedStatusWhenTheServerRefusesOrIsMissing()
    {
        using var forbidden = NewContext(new FakeActivityFeedClient
        {
            Respond = _ => OperateSectionResult<ConsoleActivityFeedPage>.Denied(OperateSectionStatus.Forbidden, "Platform admins only."),
        });
        Assert.Contains("Platform admins only.", forbidden.Render<ConsoleActivityFeed>().Markup, StringComparison.Ordinal);
        using var missing = NewContext(feed: null);
        var unbound = missing.Render<ConsoleActivityFeed>();
        Assert.Empty(unbound.FindAll("[data-feed-row]"));
        Assert.Contains("no server connection that records changes", unbound.Markup, StringComparison.Ordinal);
    }
}
