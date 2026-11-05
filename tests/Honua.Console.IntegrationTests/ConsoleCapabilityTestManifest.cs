using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

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
