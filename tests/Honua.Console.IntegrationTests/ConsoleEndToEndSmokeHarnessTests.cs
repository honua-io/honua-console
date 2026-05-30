using System.Text.Json;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// CI-runnable coverage for the Console end-to-end real-server smoke harness (issue #59). These facts run
/// on the standard runner WITHOUT Docker: they prove the live chain skips cleanly when the opt-in env is
/// absent, and that the evidence emitter records the server image, the seed profile, and
/// <c>sourceHydrated: true</c> in the shape the <c>honua-console#9</c> gate consumes — without booting a
/// container. This keeps the deliverable honestly tested even though the live chain itself only runs
/// behind the opt-in flag.
/// </summary>
public sealed class ConsoleEndToEndSmokeHarnessTests
{
    [Fact]
    public void EndToEndSmoke_SkipsCleanly_WhenOptInEnvIsAbsent()
    {
        // The standard CI runner sets neither HONUA_CONSOLE_INTEGRATION nor
        // HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS, so the smoke must report a skip reason (the SkippableFact
        // body never touches Docker). This is the contract that keeps CI green without a container.
        var integration = Environment.GetEnvironmentVariable("HONUA_CONSOLE_INTEGRATION");
        var liveServer = Environment.GetEnvironmentVariable("HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS");
        Skip.If(
            !string.IsNullOrWhiteSpace(integration) || !string.IsNullOrWhiteSpace(liveServer),
            "A live-server opt-in flag is set in this environment; the clean-skip contract is only "
            + "asserted on the standard runner.");

        var skipReason = ConsoleTrustIntegrationOptions.GetEndToEndSkipReason();
        Assert.NotNull(skipReason);
        Assert.Contains("HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS", skipReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceEmitter_RecordsServerImageSeedProfileAndSourceHydrated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"console-e2e-evidence-{Guid.NewGuid():N}.json");
        var previous = Environment.GetEnvironmentVariable("HONUA_CONSOLE_E2E_EVIDENCE_PATH");
        Environment.SetEnvironmentVariable("HONUA_CONSOLE_E2E_EVIDENCE_PATH", path);
        try
        {
            var evidence = new ConsoleEndToEndSmokeEvidence
            {
                SourceHydrated = true,
                ServerImage = "ghcr.io/honua-io/honua-server:nightly",
                ServerBaseAddress = "https://127.0.0.1:8080/",
                SeedProfile = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["catalogItemId"] = "item-123",
                    ["studioPackageRef"] = "studio-abc",
                    ["publicationId"] = "pub-456"
                },
                Steps =
                [
                    new ConsoleEndToEndSmokeStep("catalog-list", "server", "catalog", "itemId=item-123"),
                    new ConsoleEndToEndSmokeStep("studio-package-render", "server", "studio", "packageRef=studio-abc"),
                    new ConsoleEndToEndSmokeStep("operate-publishing", "server", "operate", "publicationId=pub-456")
                ]
            };

            await evidence.WriteAsync();

            Assert.True(File.Exists(path));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = document.RootElement;

            // sourceHydrated: true is the discriminator the gate uses to reject in-memory-only runs.
            Assert.True(root.GetProperty("sourceHydrated").GetBoolean());
            Assert.Equal(
                "ghcr.io/honua-io/honua-server:nightly",
                root.GetProperty("serverImage").GetString());
            Assert.Equal("console-e2e-smoke-real-server", root.GetProperty("scenario").GetString());

            // Seed profile is recorded so a failure can be reproduced against the same fixture state.
            var seed = root.GetProperty("seedProfile");
            Assert.Equal("item-123", seed.GetProperty("catalogItemId").GetString());
            Assert.Equal("studio-abc", seed.GetProperty("studioPackageRef").GetString());

            // All three cross-surface flows are recorded, in order.
            var stepIds = root.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetProperty("id").GetString() ?? string.Empty)
                .ToArray();
            Assert.Equal(["catalog-list", "studio-package-render", "operate-publishing"], stepIds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HONUA_CONSOLE_E2E_EVIDENCE_PATH", previous);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
