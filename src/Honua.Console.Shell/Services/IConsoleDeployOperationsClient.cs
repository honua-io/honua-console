using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// A 404 or 501 on these routes means the server build lacks them and is Unsupported, so the cockpit keeps its tracked-id behavior.
public interface IConsoleDeployOperationsClient
{
    // The durable operations list is newest-first.
    Task<OperateSectionResult<DeployOperationListView>> ListAsync(
        DeployOperationListQuery query,
        CancellationToken cancellationToken = default);

    // Pass false for a readiness-only probe; true adds migration, database-compatibility, and platform-release detail.
    Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
        bool includeDiagnostics = true,
        CancellationToken cancellationToken = default);

    // A 404 or 501 is Unsupported rather than success, and a 403 is Forbidden because the server mediates approval.
    Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
        CancellationToken cancellationToken = default);
}
