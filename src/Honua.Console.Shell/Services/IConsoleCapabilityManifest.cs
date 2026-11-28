using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A capability the deployment does not advertise stays unsupported even when the server exposes the endpoint, and a failed manifest read fails closed.
public interface IConsoleCapabilityManifest
{
    event Action? Changed { add { } remove { } }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    // Unknown keys are not advertised.
    bool IsAdvertised(string capabilityKey);

    // Returns the server descriptor when known from the capability manifest.
    CapabilityDescriptor? GetDescriptor(string capabilityKey) => null;

    // True when the connected server reports the capability as opt-in and currently disabled.
    bool IsOptInDisabled(string capabilityKey) =>
        GetDescriptor(capabilityKey) is { Available: false, OptInRequired: true };
}

public static class ConsoleCapabilityKeys
{
    public const string Temporal = "temporal";

    public const string DisconnectedSync = "disconnected-sync";

    public const string RealtimeAlerting = "realtime-alerting";

    public const string CrossEnvironmentPromotion = "cross-environment-promotion";

    public const string SiemInvestigations = "siem-investigations";

    public const string PersistentCatalog = "persistent-catalog";

    public static string? ServerKey(string consoleKey) =>
        ManifestBackedConsoleCapabilityManifest.Mappings.TryGetValue(consoleKey, out var serverId)
            ? serverId
            : null;
}

public sealed class ConsoleCapabilityManifest : IConsoleCapabilityManifest
{
    private readonly HashSet<string> _advertised;

    public ConsoleCapabilityManifest(IEnumerable<string>? advertisedCapabilities = null)
    {
        _advertised = new HashSet<string>(
            advertisedCapabilities ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public static ConsoleCapabilityManifest FromConfigurationList(string? capabilityList) =>
        new(SplitList(capabilityList));

    public bool IsAdvertised(string capabilityKey) =>
        !string.IsNullOrWhiteSpace(capabilityKey) && _advertised.Contains(capabilityKey);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                [',', ';', ' ', '\t', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

// Local configuration can only narrow the server manifest, and a failed or incomplete read clears every server-backed gate.
public sealed class ManifestBackedConsoleCapabilityManifest : IConsoleCapabilityManifest
{
    private static readonly IReadOnlyDictionary<string, string> ServerCapabilityByConsoleKey =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ConsoleCapabilityKeys.Temporal] = "temporal.filtering",
            [ConsoleCapabilityKeys.DisconnectedSync] = "sync.offline",
            [ConsoleCapabilityKeys.RealtimeAlerting] = "alerts.geofence",
            [ConsoleCapabilityKeys.CrossEnvironmentPromotion] = "gitops.release-manifest",
            [ConsoleCapabilityKeys.SiemInvestigations] = "ops.findings",
            [ConsoleCapabilityKeys.PersistentCatalog] = "console.content.persistent",
        };

    private readonly ICapabilityRegistryClient _registry;
    private readonly object _refreshLock = new();
    private long _refreshVersion;

    private readonly HashSet<string> _localPolicy;
    private HashSet<string> _available = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CapabilityDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public ManifestBackedConsoleCapabilityManifest(
        ICapabilityRegistryClient registry,
        IEnumerable<string>? localPolicy = null)
    {
        _registry = registry;
        _localPolicy = new HashSet<string>(localPolicy ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAdvertised(string capabilityKey)
    {
        lock (_refreshLock)
        {
            return _available.Contains(capabilityKey);
        }
    }

    public CapabilityDescriptor? GetDescriptor(string capabilityKey)
    {
        if (string.IsNullOrWhiteSpace(capabilityKey))
        {
            return null;
        }

        lock (_refreshLock)
        {
            return _descriptors.TryGetValue(capabilityKey, out var descriptor) ? descriptor : null;
        }
    }

    public bool IsOptInDisabled(string capabilityKey) =>
        GetDescriptor(capabilityKey) is { Available: false, OptInRequired: true };

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        long version;
        lock (_refreshLock)
        {
            version = ++_refreshVersion;
            _available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _descriptors = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase);
        }
        Changed?.Invoke();

        var (nextAvailable, nextDescriptors) = await ReadManifestStateAsync(cancellationToken).ConfigureAwait(false);

        lock (_refreshLock)
        {
            // A slow response from a previous binding must never replace a newer refresh.
            if (version != _refreshVersion)
            {
                return;
            }
            _available = nextAvailable;
            _descriptors = nextDescriptors;
        }
        Changed?.Invoke();
    }

    private async Task<(HashSet<string> Available, Dictionary<string, CapabilityDescriptor> Descriptors)> ReadManifestStateAsync(
        CancellationToken cancellationToken)
    {
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var descriptors = new Dictionary<string, CapabilityDescriptor>(StringComparer.OrdinalIgnoreCase);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var snapshot = await _registry.GetSnapshotAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (snapshot.Bound)
            {
                CollectMappedCapabilities(snapshot, next, descriptors);
            }
        }
        catch (Exception)
        {
            // Failure, cancellation and timeout all revoke the previous server's availability.
        }

        return (next, descriptors);
    }

    private void CollectMappedCapabilities(
        CapabilityRegistrySnapshot snapshot,
        HashSet<string> available,
        Dictionary<string, CapabilityDescriptor> descriptors)
    {
        foreach (var mapping in ServerCapabilityByConsoleKey)
        {
            var matches = snapshot.Descriptors.Where(item =>
                string.Equals(item.Id, mapping.Value, StringComparison.Ordinal)).ToArray();
            if (matches is not [var descriptor])
            {
                continue;
            }

            descriptors[mapping.Key] = descriptor;
            if (descriptor is { Supported: true, Available: true }
                && (_localPolicy.Count == 0 || _localPolicy.Contains(mapping.Key)))
            {
                available.Add(mapping.Key);
            }
        }
    }

    internal static IReadOnlyDictionary<string, string> Mappings => ServerCapabilityByConsoleKey;
}
