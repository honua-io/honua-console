using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Test manifest that advertises every deferred "exotic depth" capability so the Docker-free render
/// tests keep exercising the depth surfaces (missing-binding, bound queues, resolution) that live
/// BEHIND the first-release capability-manifest gate (<c>ConsoleCapabilityGate</c>). Without an
/// advertised capability the gated page renders the first-class "unsupported" state instead, which is
/// covered separately by <see cref="ConsoleCapabilityGateRenderTests"/>.
/// </summary>
internal static class ConsoleCapabilityTestManifest
{
    public static IConsoleCapabilityManifest All { get; } = new ConsoleCapabilityManifest(
    [
        ConsoleCapabilityKeys.Temporal,
        ConsoleCapabilityKeys.DisconnectedSync,
        ConsoleCapabilityKeys.RealtimeAlerting,
        ConsoleCapabilityKeys.CrossEnvironmentPromotion,
        ConsoleCapabilityKeys.SiemInvestigations,
        // Legacy components remain covered directly during migration. Their retired routes cannot be
        // restored by this capability; StudioPageRenderTests verifies the actual routing contract.
        ConsoleCapabilityKeys.StudioBuilders,
    ]);
}
