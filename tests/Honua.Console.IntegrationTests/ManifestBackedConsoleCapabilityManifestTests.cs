using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed class ManifestBackedConsoleCapabilityManifestTests
{
    public static TheoryData<string, string> Mappings => new()
    {
        { ConsoleCapabilityKeys.Temporal, "temporal.filtering" },
        { ConsoleCapabilityKeys.DisconnectedSync, "sync.offline" },
        { ConsoleCapabilityKeys.RealtimeAlerting, "alerts.geofence" },
        { ConsoleCapabilityKeys.CrossEnvironmentPromotion, "gitops.release-manifest" },
        { ConsoleCapabilityKeys.SiemInvestigations, "ops.findings" },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task AvailableMappedPredicate_AdvertisesSurface(string consoleKey, string serverId)
    {
        var subject = Create(consoleKey, new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor(serverId, Available: true, Supported: true, ReasonCode: null)],
        });

        await subject.RefreshAsync();

        Assert.True(subject.IsAdvertised(consoleKey));
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task UnavailableOrUnsupportedPredicate_FailsClosed(string consoleKey, string serverId)
    {
        var subject = Create(consoleKey, new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor(serverId, Available: false, Supported: true, ReasonCode: "disabled-by-configuration")],
        });

        await subject.RefreshAsync();

        Assert.False(subject.IsAdvertised(consoleKey));
    }

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task MissingManifestOrMapping_FailsClosed(string consoleKey, string serverId)
    {
        var subject = Create(consoleKey, new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor($"{serverId}.unmapped", true, true, null)],
        });

        await subject.RefreshAsync();

        Assert.False(subject.IsAdvertised(consoleKey));
    }

    [Fact]
    public async Task LocalPolicy_CanNarrowButNeverWidenServerTruth()
    {
        var unavailable = Create(ConsoleCapabilityKeys.Temporal, new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor("temporal.filtering", false, true, "disabled-by-configuration")],
        });
        await unavailable.RefreshAsync();
        Assert.False(unavailable.IsAdvertised(ConsoleCapabilityKeys.Temporal));

        var narrowed = Create(ConsoleCapabilityKeys.DisconnectedSync, new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor("temporal.filtering", true, true, null)],
        });
        await narrowed.RefreshAsync();
        Assert.False(narrowed.IsAdvertised(ConsoleCapabilityKeys.Temporal));
    }

    [Fact]
    public async Task Refresh_ReplacesPreviousServerBindingState()
    {
        var registry = new StubRegistry(new CapabilityRegistrySnapshot
        {
            Bound = true,
            Descriptors = [new CapabilityDescriptor("temporal.filtering", true, true, null)],
        });
        var subject = new ManifestBackedConsoleCapabilityManifest(registry);
        await subject.RefreshAsync();
        Assert.True(subject.IsAdvertised(ConsoleCapabilityKeys.Temporal));

        registry.Snapshot = new CapabilityRegistrySnapshot { Bound = false, State = "Unavailable" };
        await subject.RefreshAsync();
        Assert.False(subject.IsAdvertised(ConsoleCapabilityKeys.Temporal));
    }

    [Fact]
    public async Task StudioBuilders_RemainsLocalAndOutsideServerMappings()
    {
        var subject = Create(ConsoleCapabilityKeys.StudioBuilders, new CapabilityRegistrySnapshot { Bound = false });
        await subject.RefreshAsync();
        Assert.True(subject.IsAdvertised(ConsoleCapabilityKeys.StudioBuilders));
    }

    [Fact]
    public async Task StudioBuildersOnlyPolicy_DoesNotNarrowServerCapabilities()
    {
        var subject = new ManifestBackedConsoleCapabilityManifest(
            new StubRegistry(new CapabilityRegistrySnapshot
            {
                Bound = true,
                Descriptors = [
                    new CapabilityDescriptor("temporal.filtering", true, true, null),
                    new CapabilityDescriptor("sync.offline", true, true, null),
                ],
            }),
            [ConsoleCapabilityKeys.StudioBuilders]);

        await subject.RefreshAsync();

        Assert.True(subject.IsAdvertised(ConsoleCapabilityKeys.StudioBuilders));
        Assert.True(subject.IsAdvertised(ConsoleCapabilityKeys.Temporal));
        Assert.True(subject.IsAdvertised(ConsoleCapabilityKeys.DisconnectedSync));
    }

    private static ManifestBackedConsoleCapabilityManifest Create(string localKey, CapabilityRegistrySnapshot snapshot)
        => new(new StubRegistry(snapshot), [localKey]);

    private sealed class StubRegistry(CapabilityRegistrySnapshot snapshot) : ICapabilityRegistryClient
    {
        public CapabilityRegistrySnapshot Snapshot { get; set; } = snapshot;

        public Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshot);
    }
}
