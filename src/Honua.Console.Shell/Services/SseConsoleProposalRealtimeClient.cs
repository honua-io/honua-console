using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public sealed class SseConsoleProposalRealtimeClient(HttpClient http, IConsoleEnvironmentProfileStore profiles,
    IConsoleAuthorityBindingValidator? authority, ILogger<SseConsoleProposalRealtimeClient> logger)
    : SseConsoleRealtimeClient(http, profiles, authority, logger, "proposals"), IConsoleProposalRealtimeClient
{
    public event Action<ConsoleProposalEvent>? ProposalChanged;

    protected override void Dispatch(string kind, string data)
    {
        if (kind is not ("ProposalPending" or "ProposalResolved")) return;
        var wire = JsonSerializer.Deserialize(data, ConsoleAdminLiveJsonContext.Default.ProposalRealtimeWire)
            ?? throw new InvalidDataException("Missing proposal event.");
        if (string.IsNullOrWhiteSpace(wire.ProposalId)) throw new InvalidDataException("Missing proposal id.");
        ProposalChanged?.Invoke(MapEvent(kind == "ProposalPending" ? ConsoleProposalEventKind.Pending : ConsoleProposalEventKind.Resolved, wire));
    }

    internal static ConsoleProposalEvent MapEvent(ConsoleProposalEventKind kind, ProposalRealtimeWire wire) => new(
        kind, wire.ProposalId ?? string.Empty, ConsoleProposalPresentation.MapKind(wire.Kind),
        ConsoleProposalPresentation.MapStatus(wire.Status), wire.RequestedBy,
        ConsoleProposalPresentation.MapRisk(wire.RiskLevel), wire.GeneratedAt);
}
