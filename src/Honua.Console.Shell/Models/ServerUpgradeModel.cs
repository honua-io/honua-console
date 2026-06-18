using System.Globalization;

namespace Honua.Console.Shell.Models;

/// <summary>
/// The running Honua version of a target environment, read from the connected
/// server's capability manifest (<c>GET /api/v1/capabilities/manifest</c>, the
/// <c>server</c> block). Drives the server-upgrade flow's version-mismatch detection
/// and gating. Server-owned (Console Patterns Charter section 11); Console never
/// synthesizes it.
/// </summary>
public sealed record ServerVersionInfo(
    string ServerVersion,
    string ApiVersion,
    string MetadataApiVersion,
    string MetadataSchemaVersion,
    string DeploymentEnvironment,
    DateTimeOffset ObservedAt)
{
    /// <summary>The parsed running version, or null when the server reported an unparseable version string.</summary>
    public Version? ParsedVersion => SemanticVersion.TryParse(ServerVersion);
}

/// <summary>
/// How the target environment's running Honua version relates to the version the
/// operator wants to upgrade to. Drives gating: an upgrade only proceeds when the
/// target is strictly behind the requested version; an already-current or ahead
/// target is gated (no no-op or accidental downgrade).
/// </summary>
public enum ServerUpgradeMismatch
{
    /// <summary>Either the current or the requested version could not be parsed; the upgrade is gated until resolved.</summary>
    Unknown,

    /// <summary>The target is strictly behind the requested version — a real upgrade. Proceed is enabled.</summary>
    Behind,

    /// <summary>The target already runs the requested version — nothing to upgrade. Proceed is gated.</summary>
    Current,

    /// <summary>The target runs a newer version than requested — this would be a downgrade. Proceed is gated.</summary>
    Ahead,
}

/// <summary>
/// The computed server-upgrade plan for a target environment: the detected current
/// version, the requested target version, the mismatch classification, and whether the
/// upgrade is allowed to proceed. This is a pure projection (no I/O) so the
/// version-mismatch gate is unit-testable in isolation; the actual upgrade is actuated
/// as a deploy-control operation through the approval surface, never by this record.
/// </summary>
public sealed record ServerUpgradePlan(
    ServerVersionInfo? Current,
    string RequestedVersion,
    ServerUpgradeMismatch Mismatch)
{
    /// <summary>
    /// True only when the target is strictly behind the requested version. The
    /// already-current, ahead (downgrade), and unknown cases are all gated so the
    /// operator never submits a no-op or an accidental downgrade as an "upgrade".
    /// </summary>
    public bool CanProceed => Mismatch == ServerUpgradeMismatch.Behind;

    /// <summary>A neutral, operator-facing reason the upgrade is gated, or empty when it can proceed.</summary>
    public string GateReason => Mismatch switch
    {
        ServerUpgradeMismatch.Behind => string.Empty,
        ServerUpgradeMismatch.Current =>
            "The target environment already runs the requested version. There is nothing to upgrade.",
        ServerUpgradeMismatch.Ahead =>
            "The target environment runs a newer version than requested. Upgrading would be a downgrade; "
            + "use a rollback operation instead.",
        _ =>
            "The current or requested version could not be parsed as a Honua version, so the upgrade path "
            + "cannot be verified. Confirm the target's version before proceeding.",
    };

    /// <summary>
    /// Builds an upgrade plan by classifying the requested version against the detected
    /// current version. An unparseable current or requested version yields
    /// <see cref="ServerUpgradeMismatch.Unknown"/> (gated), never an optimistic proceed.
    /// </summary>
    public static ServerUpgradePlan Create(ServerVersionInfo? current, string? requestedVersion)
    {
        var requested = (requestedVersion ?? string.Empty).Trim();
        var requestedParsed = SemanticVersion.TryParse(requested);
        var currentParsed = current?.ParsedVersion;

        ServerUpgradeMismatch mismatch;
        if (currentParsed is null || requestedParsed is null)
        {
            mismatch = ServerUpgradeMismatch.Unknown;
        }
        else
        {
            var comparison = currentParsed.CompareTo(requestedParsed);
            mismatch = comparison switch
            {
                < 0 => ServerUpgradeMismatch.Behind,
                0 => ServerUpgradeMismatch.Current,
                _ => ServerUpgradeMismatch.Ahead,
            };
        }

        return new ServerUpgradePlan(current, requested, mismatch);
    }
}

/// <summary>
/// Presentation helpers for the server-upgrade surface so the component renders neutral
/// labels and status classes without re-deriving semantics in markup.
/// </summary>
public static class ServerUpgradePresentation
{
    public static string Label(ServerUpgradeMismatch mismatch) => mismatch switch
    {
        ServerUpgradeMismatch.Behind => "upgrade available",
        ServerUpgradeMismatch.Current => "already current",
        ServerUpgradeMismatch.Ahead => "newer than requested",
        _ => "version unknown",
    };

    public static string StateClass(ServerUpgradeMismatch mismatch) => mismatch switch
    {
        ServerUpgradeMismatch.Behind => "console-state-info",
        ServerUpgradeMismatch.Current => "console-state-success",
        ServerUpgradeMismatch.Ahead => "console-state-warning",
        _ => "console-state-neutral",
    };
}

/// <summary>
/// Lenient Honua version parsing. Honua server versions come from
/// <c>AssemblyInformationalVersion</c>, which may carry a build/commit suffix
/// (<c>1.4.2+abc123</c>) or an SemVer pre-release (<c>1.4.2-rc.1</c>). This trims the
/// metadata/pre-release suffix and parses the numeric <c>major.minor[.patch[.build]]</c>
/// core so comparisons are stable; an unrecognized string yields null (treated as
/// unknown by the gate, never optimistically equal).
/// </summary>
public static class SemanticVersion
{
    public static Version? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var core = value.Trim();

        // Drop a leading 'v' (e.g. v1.4.2).
        if (core.Length > 1 && (core[0] == 'v' || core[0] == 'V'))
        {
            core = core[1..];
        }

        // Drop build metadata (+...) and SemVer pre-release (-...) suffixes.
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            core = core[..plus];
        }

        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            core = core[..dash];
        }

        // Version.Parse requires at least major.minor; pad a bare "1" to "1.0".
        if (!core.Contains('.', StringComparison.Ordinal))
        {
            if (!int.TryParse(core, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return null;
            }

            core += ".0";
        }

        return Version.TryParse(core, out var parsed) ? parsed : null;
    }
}
