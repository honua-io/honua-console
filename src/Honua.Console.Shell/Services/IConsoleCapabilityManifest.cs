namespace Honua.Console.Shell.Services;

/// <summary>
/// Reports which capability-gated "exotic depth" surfaces the connected deployment ADVERTISES for
/// this release. The first-release cut-line
/// (<c>docs/roadmap/FIRST_RELEASE_STRATEGY_AND_CUT_LINE.md</c>) defers several depth capabilities
/// (temporal "git over data", disconnected sync conflict review, realtime/geofence alerting,
/// cross-environment promotion, full SIEM / investigations / AI DevOps advisory). They are designed
/// broad in the contracts but not turned on until a customer pulls the trigger.
///
/// Deferred capabilities are ABSENT from the advertised set by default, so their Console surfaces
/// render the first-class "unsupported" state (via <see cref="ConsoleCapabilityKeys"/> +
/// <c>ConsoleCapabilityGate</c>) instead of live UI — and light up with no re-architecture once the
/// capability is advertised. This is layered ABOVE the existing missing-binding gating: the
/// missing-binding state answers "is a server contract bound?", whereas this manifest answers "does
/// this release advertise the capability at all?", so a server that happens to serve a deferred
/// endpoint still renders unsupported until the deployment opts the capability in.
///
/// Server-backed gates resolve from <c>GET /api/v1/capabilities/manifest</c> and fail closed.
/// <c>Honua:Console:Capabilities</c> is an optional intersection-only local policy; it cannot
/// make a server-unavailable capability available. <c>studio-builders</c> remains local-only.
/// </summary>
public interface IConsoleCapabilityManifest
{
    /// <summary>Refreshes capability truth for the current server binding.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the connected deployment advertises <paramref name="capabilityKey"/> for this
    /// release (see <see cref="ConsoleCapabilityKeys"/>). Unknown/unadvertised keys return false so
    /// the surface renders the "unsupported" state.
    /// </summary>
    bool IsAdvertised(string capabilityKey);
}

/// <summary>Stable capability keys for the deferred, capability-gated exotic-depth surfaces.</summary>
public static class ConsoleCapabilityKeys
{
    /// <summary>Temporal "git over data": as-of / diff / rollback / feature history.</summary>
    public const string Temporal = "temporal";

    /// <summary>Disconnected sync conflict review (replica three-way merge).</summary>
    public const string DisconnectedSync = "disconnected-sync";

    /// <summary>Realtime / geofence alerting rule authoring.</summary>
    public const string RealtimeAlerting = "realtime-alerting";

    /// <summary>Cross-environment promotion (dev → staging → prod fleet).</summary>
    public const string CrossEnvironmentPromotion = "cross-environment-promotion";

    /// <summary>Full SIEM / investigations / AI DevOps advisory over Operate event volume.</summary>
    public const string SiemInvestigations = "siem-investigations";

    /// <summary>
    /// The Console's non-realtime Studio builder surfaces (Studio home / inline authoring shell, and
    /// the map, app, dashboard, analysis, query, report-from-prompt, form-from-prompt, and
    /// workflow-from-prompt builders).
    ///
    /// SHELVED, not deleted. "Studio" is now the realtime, SDK-driven app builder, which is not a
    /// Console surface; the Console keeps its back-office roles (Catalog, Operate, Share, support,
    /// publish/approval flows). The pages, services, and generation clients stay in the tree behind
    /// this capability so the decision is reversible: advertise <c>studio-builders</c> in
    /// <c>Honua:Console:Capabilities</c> / <c>HONUA_CONSOLE_CAPABILITIES</c> and every surface lights
    /// back up unchanged.
    /// </summary>
    public const string StudioBuilders = "studio-builders";
}

/// <summary>
/// Static <see cref="IConsoleCapabilityManifest"/> used by tests and local-only hosts.
/// </summary>
public sealed class ConsoleCapabilityManifest : IConsoleCapabilityManifest
{
    private readonly HashSet<string> _advertised;

    public ConsoleCapabilityManifest(IEnumerable<string>? advertisedCapabilities = null)
    {
        _advertised = new HashSet<string>(
            advertisedCapabilities ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds a manifest from a delimited configuration list (comma / semicolon / whitespace
    /// separated), e.g. <c>"temporal, realtime-alerting"</c>. Null/blank advertises nothing.
    /// </summary>
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

/// <summary>
/// Resolves server-backed Console gates from the live server manifest. Local
/// configuration is intersection-only; <c>studio-builders</c> remains local.
/// Failed or incomplete manifest reads clear every server-backed gate.
/// </summary>
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
        };

    private readonly ICapabilityRegistryClient _registry;
    private readonly HashSet<string> _localPolicy;
    private readonly HashSet<string> _serverPolicy;
    private HashSet<string> _available = new(StringComparer.OrdinalIgnoreCase);

    public ManifestBackedConsoleCapabilityManifest(
        ICapabilityRegistryClient registry,
        IEnumerable<string>? localPolicy = null)
    {
        _registry = registry;
        _localPolicy = new HashSet<string>(localPolicy ?? [], StringComparer.OrdinalIgnoreCase);
        _serverPolicy = new HashSet<string>(
            _localPolicy.Where(key => !string.Equals(
                key,
                ConsoleCapabilityKeys.StudioBuilders,
                StringComparison.OrdinalIgnoreCase)),
            StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAdvertised(string capabilityKey)
    {
        if (string.Equals(capabilityKey, ConsoleCapabilityKeys.StudioBuilders, StringComparison.OrdinalIgnoreCase))
        {
            return _localPolicy.Contains(capabilityKey);
        }

        return _available.Contains(capabilityKey);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // A refresh is fail-closed while the current binding is being read. This also prevents a
        // timed-out environment switch from leaving the previous server's capabilities visible.
        Interlocked.Exchange(ref _available, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var snapshot = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (snapshot.Bound)
        {
            foreach (var mapping in ServerCapabilityByConsoleKey)
            {
                var descriptor = snapshot.Descriptors.FirstOrDefault(item =>
                    string.Equals(item.Id, mapping.Value, StringComparison.Ordinal));
                if (descriptor is { Supported: true, Available: true }
                    && (_serverPolicy.Count == 0 || _serverPolicy.Contains(mapping.Key)))
                {
                    next.Add(mapping.Key);
                }
            }
        }

        Interlocked.Exchange(ref _available, next);
    }

    internal static IReadOnlyDictionary<string, string> Mappings => ServerCapabilityByConsoleKey;
}
