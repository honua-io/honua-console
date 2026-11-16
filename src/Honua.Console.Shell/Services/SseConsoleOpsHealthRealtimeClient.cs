using System.Text.Json;
using Honua.Console.Contracts;
using Microsoft.Extensions.Logging;

namespace Honua.Console.Shell.Services;

public sealed class SseConsoleOpsHealthRealtimeClient(HttpClient http, IConsoleEnvironmentProfileStore profiles,
    IConsoleAuthorityBindingValidator? authority, ILogger<SseConsoleOpsHealthRealtimeClient> logger)
    : SseConsoleRealtimeClient(http, profiles, authority, logger, "ops-health"), IConsoleOpsHealthRealtimeClient
{
    public event Action<OpsHealthSnapshotResponse>? SnapshotReceived;

    protected override void Dispatch(string kind, string data)
    {
        if (kind != "OpsHealthSnapshot") return;
        var snapshot = JsonSerializer.Deserialize(data, OpsHealthJsonContext.Default.OpsHealthSnapshotResponse)
            ?? throw new InvalidDataException("Missing health event.");
        SnapshotReceived?.Invoke(snapshot);
    }
}
