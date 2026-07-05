using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads honua-server's consolidated ops-health snapshot (group
/// <c>/api/v1/admin/observability</c>, admin-authorized via <c>X-API-Key</c>, bare JSON —
/// NO ApiResponse envelope) from the active environment profile's server. The read is
/// independently permissioned/optional, so it returns an
/// <see cref="OperateSectionResult{T}"/> whose status drives the shared
/// missing/forbidden/unsupported/unavailable surfaces, mirroring
/// <see cref="IConsoleMonitoringMetricsClient"/>. Per the Console Patterns Charter
/// section 11 the client never returns seeded data; with no environment bound the read
/// returns a missing-binding result.
/// </summary>
public interface IConsoleOpsHealthClient
{
    /// <summary>Reads the consolidated ops-health snapshot for the active environment.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The snapshot, or a non-allowed section status.</returns>
    Task<OperateSectionResult<OpsHealthSnapshotResponse>> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}
