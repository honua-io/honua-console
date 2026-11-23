using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleActivityPresentationTests
{
    private static ObservabilityAuditRecordResponse Row(string actorType, string? agent = null) => new()
    {
        AuditId = 1,
        Actor = "kai@example.org",
        ActorType = actorType,
        ActorAgent = agent,
        ResourceType = "operation_proposal",
        ResourceId = "p-47",
        Action = "operation.approval_denied",
        Outcome = "Success",
        CorrelationId = "corr-1",
    };

    [Theory]
    [InlineData("UserId", "Claude Desktop", "Claude Desktop | Agent · on behalf of kai@example.org | agent")]
    [InlineData("ApiKey", "catalog-curator", "catalog-curator | Agent · API key | agent")]
    [InlineData("UserId", "kai@example.org", "kai@example.org | Agent | agent")]
    [InlineData("System", "catalog-curator", "catalog-curator | Agent | agent")]
    [InlineData("UserId", null, "kai@example.org | Person | person")]
    [InlineData("ApiKey", " ", "API key | kai@example.org | key")]
    [InlineData("System", null, "kai@example.org | System | system")]
    [InlineData("Anonymous", null, "Anonymous | Not signed in | person")]
    public void Actor_NamesThePersonAgentOrKey(string actorType, string? agent, string expected)
    {
        var actor = ConsoleActivityPresentation.Actor(Row(actorType, agent));

        Assert.Equal(expected, $"{actor.Name} | {actor.Channel} | {actor.Kind}");
    }

    [Theory]
    [InlineData(ConsoleActivityActorFilter.Any, null, "?eventType=AdminAction,ConfigChange,DataDelete&pageSize=25")]
    [InlineData(ConsoleActivityActorFilter.Agent, null, "?eventType=AdminAction,ConfigChange,DataDelete&pageSize=25")]
    [InlineData(ConsoleActivityActorFilter.Person, "a b", "?eventType=AdminAction,ConfigChange,DataDelete&pageSize=25&actorType=UserId&cursor=a%20b")]
    [InlineData(ConsoleActivityActorFilter.ApiKey, null, "?eventType=AdminAction,ConfigChange,DataDelete&pageSize=25&actorType=ApiKey")]
    public void QueryString_AsksForChangesOnly(ConsoleActivityActorFilter actor, string? cursor, string expected)
    {
        Assert.Equal(expected, ConsoleActivityPresentation.QueryString(new ConsoleActivityFeedQuery(actor, cursor)));
    }

    [Theory]
    [InlineData(ConsoleActivityActorFilter.Person, "UserId")]
    [InlineData(ConsoleActivityActorFilter.ApiKey, "ApiKey")]
    [InlineData(ConsoleActivityActorFilter.System, "System")]
    public void Visible_KeepsOnlyTheSelectedActorKind(ConsoleActivityActorFilter filter, string actorType)
    {
        var principal = Row(actorType, " ");
        var agent = Row(actorType, "Cursor");
        ObservabilityAuditRecordResponse[] rows = [agent, principal, Row("Anonymous")];

        Assert.Same(principal, Assert.Single(ConsoleActivityPresentation.Visible(rows, filter)));
        Assert.Same(agent, Assert.Single(ConsoleActivityPresentation.Visible(rows, ConsoleActivityActorFilter.Agent)));
        Assert.Same(rows, ConsoleActivityPresentation.Visible(rows, ConsoleActivityActorFilter.Any));
    }

    [Fact]
    public void WhatAndReference_LinkProposalsToTheirReview()
    {
        var proposal = Row("UserId");
        var config = Row("UserId") with { ResourceType = "connection", ResourceId = null };

        Assert.Equal("Operation proposal p-47", ConsoleActivityPresentation.What(proposal));
        Assert.Equal("Operation approval denied", ConsoleActivityPresentation.Humanize(proposal.Action));
        Assert.Equal(CorrelationIdRoutes.Resolve(CorrelationIdKind.ProposalId, "p-47"), ConsoleActivityPresentation.ReferenceHref(proposal));
        Assert.Equal("proposal", ConsoleActivityPresentation.ReferenceLabel(proposal));
        Assert.Equal("Connection", ConsoleActivityPresentation.What(config));
        Assert.Equal(CorrelationIdRoutes.Resolve(CorrelationIdKind.CorrelationId, "corr-1"), ConsoleActivityPresentation.ReferenceHref(config));
        Assert.Null(ConsoleActivityPresentation.ReferenceHref(config with { CorrelationId = " " }));
    }

    [Theory]
    [InlineData(0, 0, "No changes on page 1")]
    [InlineData(1, 1, "1 change on page 2, newest first")]
    [InlineData(0, 25, "25 changes on page 1, newest first")]
    public void Range_CountsTheChangesOnThePage(int pagesBefore, int shown, string expected)
    {
        Assert.Equal(expected, ConsoleActivityPresentation.Range(pagesBefore, shown));
    }
}
