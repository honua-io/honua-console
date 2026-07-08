using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Active when the deploy cockpit's collection-level surfaces (operations list, preflight,
/// converge) have no live binding (no server configured for this Console build). Per the
/// Console missing-binding convention, every call renders an explicit unsupported state
/// rather than mocking a round-trip. Missing-binding is a first-class state — no crash, no
/// fabricated data. Mirrors <see cref="UnsupportedConsoleDeployApprovalClient"/>.
/// </summary>
public sealed class UnsupportedConsoleDeployOperationsClient : IConsoleDeployOperationsClient
{
    private const string Message =
        "The deploy-operations list/preflight/converge surfaces are not configured for this Console build. "
        + "Connect an environment to read deploy operations.";

    public Task<OperateSectionResult<DeployOperationListView>> ListAsync(
        DeployOperationListQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployOperationListView>.Denied(OperateSectionStatus.Unsupported, Message));

    public Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
        bool includeDiagnostics = true,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<DeployPreflightView>.Denied(OperateSectionStatus.Unsupported, Message));

    public Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<PlatformReleaseConvergeView>.Denied(OperateSectionStatus.Unsupported, Message));
}
