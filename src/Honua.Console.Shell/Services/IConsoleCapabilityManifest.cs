namespace Honua.Console.Shell.Services;

// A capability the deployment does not advertise stays unsupported even when the server exposes the endpoint, and a failed manifest read fails closed.
public interface IConsoleCapabilityManifest
{
    event Action? Changed { add { } remove { } }

    Task RefreshAsync(CancellationToken cancellationToken = default);

    // Unknown keys are not advertised.
    bool IsAdvertised(string capabilityKey);
}

public static class ConsoleCapabilityKeys
{
    public const string Temporal = "temporal";

    public const string DisconnectedSync = "disconnected-sync";

    public const string RealtimeAlerting = "realtime-alerting";

    public const string CrossEnvironmentPromotion = "cross-environment-promotion";

    public const string SiemInvestigations = "siem-investigations";

    public const string PersistentCatalog = "persistent-catalog";
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

    // A null or blank list advertises nothing.
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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        long version;
        lock (_refreshLock)
        {
            version = ++_refreshVersion;
            _available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        Changed?.Invoke();

        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var snapshot = await _registry.GetSnapshotAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (snapshot.Bound)
            {
                foreach (var mapping in ServerCapabilityByConsoleKey)
                {
                    var descriptors = snapshot.Descriptors.Where(item =>
                        string.Equals(item.Id, mapping.Value, StringComparison.Ordinal)).ToArray();
                    if (descriptors is [{ Supported: true, Available: true }]
                        && (_localPolicy.Count == 0 || _localPolicy.Contains(mapping.Key)))
                    {
                        next.Add(mapping.Key);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Failure, cancellation and timeout all revoke the previous server's availability.
        }

        lock (_refreshLock)
        {
            // A slow response from a previous binding must never replace a newer refresh.
            if (version != _refreshVersion)
            {
                return;
            }
            _available = next;
        }
        Changed?.Invoke();
    }

    internal static IReadOnlyDictionary<string, string> Mappings => ServerCapabilityByConsoleKey;
}
