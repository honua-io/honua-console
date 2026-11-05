using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

// The manifest advertises capabilities because a gated page renders unsupported when the capability is absent, and these tests would never reach the surface.
internal static class ConsoleCapabilityTestManifest
{
    public static IConsoleCapabilityManifest All { get; } = new ConsoleCapabilityManifest(
    [
        ConsoleCapabilityKeys.Temporal,
        ConsoleCapabilityKeys.DisconnectedSync,
        ConsoleCapabilityKeys.RealtimeAlerting,
        ConsoleCapabilityKeys.CrossEnvironmentPromotion,
        ConsoleCapabilityKeys.SiemInvestigations,
        ConsoleCapabilityKeys.PersistentCatalog,
    ]);
}
