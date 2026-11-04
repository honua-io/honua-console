using System.Globalization;

namespace Honua.Console.Shell.Models;

public sealed record ServerVersionInfo(
    string ServerVersion,
    string ApiVersion,
    string MetadataApiVersion,
    string MetadataSchemaVersion,
    string DeploymentEnvironment,
    DateTimeOffset ObservedAt)
{
    public Version? ParsedVersion => SemanticVersion.TryParse(ServerVersion);
}

public enum ServerUpgradeMismatch
{
    Unknown,

    Behind,

    Current,

    Ahead,
}

public sealed record ServerUpgradePlan(
    ServerVersionInfo? Current,
    string RequestedVersion,
    ServerUpgradeMismatch Mismatch)
{
    public bool CanProceed => Mismatch == ServerUpgradeMismatch.Behind;

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
