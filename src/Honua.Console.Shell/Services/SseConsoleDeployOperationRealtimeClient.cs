using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public sealed class SseConsoleDeployOperationRealtimeClient(HttpClient http, IConsoleEnvironmentProfileStore profiles,
    IConsoleAuthorityBindingValidator? authority, ILogger<SseConsoleDeployOperationRealtimeClient> logger)
    : SseConsoleRealtimeClient(http, profiles, authority, logger, "deploy-operations"), IConsoleDeployOperationRealtimeClient
{
    public event Action<ConsoleDeployOperationRealtimeEvent>? OperationChanged;

    protected override void Dispatch(string kind, string data)
    {
        if (kind != "DeployOperationTransition") return;
        var wire = JsonSerializer.Deserialize(data, ConsoleAdminLiveJsonContext.Default.DeployOperationTransitionWire)
            ?? throw new InvalidDataException("Missing deployment event.");
        if (string.IsNullOrWhiteSpace(wire.OperationId)) throw new InvalidDataException("Missing operation id.");
        OperationChanged?.Invoke(new(wire.OperationId, wire.TransitionKind ?? string.Empty,
            wire.Status ?? string.Empty, wire.CorrelationId, wire.OccurredAt));
    }
}
