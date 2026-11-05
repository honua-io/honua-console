using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleProposalRealtimeMapperTests
{
    [Fact]
    public void MapEvent_ProjectsPendingPayload()
    {
        var wire = new ProposalRealtimeWire
        {
            ProposalId = "prop-1",
            Kind = "DataImport",
            Status = "AwaitingApproval",
            RequestedBy = "agent.ingest",
            RiskLevel = "Medium",
            GeneratedAt = DateTimeOffset.Parse("2026-06-28T10:00:00Z")
        };

        var evt = SignalRConsoleProposalRealtimeClient.MapEvent(ConsoleProposalEventKind.Pending, wire);

        Assert.Equal(ConsoleProposalEventKind.Pending, evt.EventKind);
        Assert.Equal("prop-1", evt.ProposalId);
        Assert.Equal(ConsoleProposalKind.DataImport, evt.Kind);
        Assert.Equal(ConsoleProposalStatus.AwaitingApproval, evt.Status);
        Assert.Equal(ConsoleProposalRisk.Medium, evt.RiskLevel);
        Assert.Equal("agent.ingest", evt.RequestedBy);
    }

    [Fact]
    public void MapEvent_ProjectsResolvedPayload_AndToleratesUnknownWireValues()
    {
        var wire = new ProposalRealtimeWire
        {
            ProposalId = "prop-2",
            Kind = "something-new",
            Status = "weird",
            RiskLevel = null,
            GeneratedAt = DateTimeOffset.Parse("2026-06-28T11:00:00Z")
        };

        var evt = SignalRConsoleProposalRealtimeClient.MapEvent(ConsoleProposalEventKind.Resolved, wire);

        Assert.Equal(ConsoleProposalEventKind.Resolved, evt.EventKind);
        Assert.Equal("prop-2", evt.ProposalId);
        Assert.Equal(ConsoleProposalKind.Unknown, evt.Kind);
        Assert.Equal(ConsoleProposalStatus.Unknown, evt.Status);
        Assert.Equal(ConsoleProposalRisk.Unknown, evt.RiskLevel);
        Assert.Null(evt.RequestedBy);
    }
}
