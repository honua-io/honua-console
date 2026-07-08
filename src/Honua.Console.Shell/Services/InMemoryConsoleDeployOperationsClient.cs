using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Test/demo-only in-memory <see cref="IConsoleDeployOperationsClient"/>. It is NEVER the DI
/// default (Console Patterns Charter section 11); it exists so bUnit/component tests and
/// explicit demo shells can drive the deploy cockpit's list/preflight/converge surfaces
/// without a backend. Mirrors <see cref="InMemoryConsoleDeployApprovalClient"/>'s shape.
/// </summary>
public sealed class InMemoryConsoleDeployOperationsClient : IConsoleDeployOperationsClient
{
    private readonly List<DeployOperationProposal> _operations;
    private readonly OperateSectionResult<DeployPreflightView> _preflight;
    private readonly OperateSectionResult<PlatformReleaseConvergeView> _converge;

    public InMemoryConsoleDeployOperationsClient(
        IEnumerable<DeployOperationProposal>? seed = null,
        OperateSectionResult<DeployPreflightView>? preflight = null,
        OperateSectionResult<PlatformReleaseConvergeView>? converge = null)
    {
        _operations = (seed ?? []).ToList();
        _preflight = preflight ?? OperateSectionResult<DeployPreflightView>.Allowed(ReadyPreflight());
        // Every real server available today has no converge route (honua-server#2564 is not yet
        // merged); the default here matches that honest capability-detected state.
        _converge = converge ?? OperateSectionResult<PlatformReleaseConvergeView>.Denied(
            OperateSectionStatus.Unsupported,
            "The honua-server platform-release converge API is not available on the connected server (older server build, or the capability has not merged yet).");
    }

    public Task<OperateSectionResult<DeployOperationListView>> ListAsync(
        DeployOperationListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = _operations
            .Where(op => string.IsNullOrWhiteSpace(query.Status) || string.Equals(op.RawStatus, query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(op => string.IsNullOrWhiteSpace(query.Kind) || string.Equals(op.Kind, query.Kind, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(op => op.CreatedAt)
            .ToArray();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Max(1, query.PageSize);
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return Task.FromResult(OperateSectionResult<DeployOperationListView>.Allowed(new DeployOperationListView(
            pageItems,
            page,
            pageSize,
            filtered.Length,
            filtered.Length > page * pageSize)));
    }

    public Task<OperateSectionResult<DeployPreflightView>> GetPreflightAsync(
        bool includeDiagnostics = true,
        CancellationToken cancellationToken = default) => Task.FromResult(_preflight);

    public Task<OperateSectionResult<PlatformReleaseConvergeView>> ConvergeAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(_converge);

    private static DeployPreflightView ReadyPreflight() => new(
        Status: new OperateStatus("healthy", "Ready for a coordinated deploy."),
        ReadyForCoordinatedDeploy: true,
        Message: "Instance is ready for coordinated deployment.",
        DiagnosticsIncluded: false,
        UpgradeRequired: false,
        PlanAvailable: true,
        PendingScripts: [],
        ExecutedButNotDiscoveredScripts: [],
        PlanError: null,
        DatabaseCompatible: true,
        DatabaseWarnings: [],
        DatabaseErrorMessage: null,
        PlatformReleaseDeclared: false,
        PlatformReleaseCoVersioned: true,
        SkewedPlaneIds: []);
}
