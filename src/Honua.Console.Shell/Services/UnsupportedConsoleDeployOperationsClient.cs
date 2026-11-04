using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

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
