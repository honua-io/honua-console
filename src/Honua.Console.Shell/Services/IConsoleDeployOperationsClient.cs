using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the honua-server deploy-control collection-level surfaces the deploy cockpit needs
/// beyond a single tracked operation id (console#290): the paged operations list
/// (<c>GET /api/v1/admin/deploy/operations</c>, honua-server PR #2577), the preflight gate
/// (<c>GET /api/v1/admin/deploy/preflight</c>), and the speculative platform-release converge
/// action (<c>POST /api/v1/admin/platform-release/converge</c>, honua-server#2564).
///
/// Every new route here is feature-detected: a connected server that does not yet expose it
/// (older build, or — for converge — before honua-server#2564 merges) returns 404/501, which
/// this client maps to <see cref="OperateSectionStatus.Unsupported"/> so the cockpit degrades
/// to its pre-existing tracked-id behavior rather than erroring. Per the Console Patterns
/// Charter (section 11), no standing in-memory source backs this client; with no environment
/// bound every call returns a missing-binding result.
/// </summary>
public interface IConsoleDeployOperationsClient
{
    /// <summary>Reads a page of the durable deploy-operations list, newest-first.</summary>
    Task<OperateSectionResult<DeployOperationListView>> ListAsync(
        DeployOperationListQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the deploy preflight gate. <paramref name="includeDiagnostics"/> requests the
    /// migration/database-compatibility/platform-release detail the upgrade card's gate needs;
    /// omit it for a cheaper readiness-only probe.
    /// </summary>
    Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
        bool includeDiagnostics = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Actuates the platform-release converge action. Speculative against
    /// honua-server#2564 (not yet merged at the time this client was authored) — every server
    /// available today returns 404/501, surfaced as <see cref="OperateSectionStatus.Unsupported"/>
    /// so the converge card renders its capability-gated unavailable state rather than a fake
    /// success. The action is approval-mediated server-side; a 403 surfaces as
    /// <see cref="OperateSectionStatus.Forbidden"/> exactly like the existing rollback gate.
    /// </summary>
    Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
        CancellationToken cancellationToken = default);
}
