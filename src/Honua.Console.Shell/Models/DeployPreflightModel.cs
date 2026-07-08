using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// The deploy preflight gate for the upgrade card (console#290 acceptance criterion 3):
/// before submitting a server-version upgrade, the operator sees whether pending contract
/// migration scripts or a non-co-versioned platform release would block the coordinated
/// deploy, with a clear explanation rather than a silent submit. Fields mirror
/// <c>GET /api/v1/admin/deploy/preflight?includeDiagnostics=true</c> exactly; a field the
/// server omits (nullable) is preserved as <see langword="null"/> rather than defaulted, per
/// the honua-server PR #2577 null-omission contract this endpoint shares.
/// </summary>
public sealed record DeployPreflightView(
    OperateStatus Status,
    bool ReadyForCoordinatedDeploy,
    string Message,
    bool DiagnosticsIncluded,
    bool UpgradeRequired,
    bool PlanAvailable,
    IReadOnlyList<string> PendingScripts,
    IReadOnlyList<string> ExecutedButNotDiscoveredScripts,
    string? PlanError,
    bool DatabaseCompatible,
    IReadOnlyList<string> DatabaseWarnings,
    string? DatabaseErrorMessage,
    bool PlatformReleaseDeclared,
    bool PlatformReleaseCoVersioned,
    IReadOnlyList<string> SkewedPlaneIds)
{
    /// <summary>
    /// Whether the preflight gate blocks a coordinated-deploy submit: pending contract
    /// migration scripts, an incompatible database, or a declared platform release that is
    /// not co-versioned across planes. Mirrors the same signal the connected server itself
    /// uses for <c>readyForCoordinatedDeploy</c>, but keeps the plane-skew reason visible
    /// even when the server call omitted <c>includeDiagnostics</c> (in which case the
    /// upstream boolean is the sole authority and this is left in lock-step with it).
    /// </summary>
    public bool BlocksSubmit => !ReadyForCoordinatedDeploy;

    /// <summary>Human-readable reasons the gate is blocking submit, for the upgrade card's explanation.</summary>
    public IReadOnlyList<string> BlockingReasons
    {
        get
        {
            if (!BlocksSubmit)
            {
                return [];
            }

            var reasons = new List<string>();
            if (PendingScripts.Count > 0)
            {
                reasons.Add($"{PendingScripts.Count} pending contract migration script(s) must run before this environment can coordinate a deploy.");
            }

            if (!DatabaseCompatible)
            {
                reasons.Add(string.IsNullOrWhiteSpace(DatabaseErrorMessage)
                    ? "The database does not meet Honua's compatibility requirements."
                    : DatabaseErrorMessage);
            }

            if (PlatformReleaseDeclared && !PlatformReleaseCoVersioned)
            {
                reasons.Add(SkewedPlaneIds.Count > 0
                    ? $"The declared platform release is skewed on {SkewedPlaneIds.Count} plane(s): {string.Join(", ", SkewedPlaneIds)}."
                    : "The declared platform release is not co-versioned across planes.");
            }

            if (reasons.Count == 0)
            {
                reasons.Add(string.IsNullOrWhiteSpace(Message)
                    ? "The connected server reports this environment is not ready for a coordinated deploy."
                    : Message);
            }

            return reasons;
        }
    }
}

/// <summary>Maps the server preflight response onto the Console gate view.</summary>
public static class DeployPreflightMapper
{
    public static DeployPreflightView Map(DeployPreflightResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var migration = response.Migration;
        var db = response.DatabaseCompatibility;
        var platformRelease = response.PlatformRelease;

        return new DeployPreflightView(
            Status: StatusFor(response.Status, response.ReadyForCoordinatedDeploy),
            ReadyForCoordinatedDeploy: response.ReadyForCoordinatedDeploy,
            Message: response.Message,
            DiagnosticsIncluded: migration is not null || db is not null || platformRelease is not null,
            UpgradeRequired: migration?.UpgradeRequired ?? false,
            PlanAvailable: migration?.PlanAvailable ?? false,
            PendingScripts: migration?.PendingScripts ?? [],
            ExecutedButNotDiscoveredScripts: migration?.ExecutedButNotDiscoveredScripts ?? [],
            PlanError: migration?.PlanError,
            DatabaseCompatible: db?.IsCompatible ?? true,
            DatabaseWarnings: db?.Warnings ?? [],
            DatabaseErrorMessage: db?.ErrorMessage,
            PlatformReleaseDeclared: platformRelease?.ReleaseDeclared ?? false,
            PlatformReleaseCoVersioned: platformRelease?.IsCoVersioned ?? true,
            SkewedPlaneIds: platformRelease?.SkewedIds ?? []);
    }

    private static OperateStatus StatusFor(string rawStatus, bool ready) =>
        ready
            ? new OperateStatus("healthy", string.IsNullOrWhiteSpace(rawStatus) ? "Ready for a coordinated deploy." : rawStatus)
            : new OperateStatus("warning", string.IsNullOrWhiteSpace(rawStatus) ? "Not ready for a coordinated deploy." : rawStatus);
}
