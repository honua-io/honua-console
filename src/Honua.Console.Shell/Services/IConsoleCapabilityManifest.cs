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
/// Advertised-set source (interim): the <c>Honua:Console:Capabilities</c> configuration list,
/// threaded through <c>AddHonuaConsoleShell</c>. This is the same seam the honua-server
/// capability-manifest document (<c>GET /api/v1/capabilities/manifest</c>) will feed once its
/// full-document consumption lands — swapping the source is a registration change, not a rewrite.
/// </summary>
public interface IConsoleCapabilityManifest
{
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
}

/// <summary>
/// Default <see cref="IConsoleCapabilityManifest"/> backed by an explicit advertised-capability set
/// (interim source = the <c>Honua:Console:Capabilities</c> configuration list). An empty set — the
/// default for first release — means every deferred capability renders as unsupported.
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

    private static IEnumerable<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                [',', ';', ' ', '\t', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
