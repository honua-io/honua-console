using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsAutonomyClient
{
    Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsAutonomySnapshot>> LoadForEnvironmentAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<OpsAutonomySnapshot>.Denied(
            OperateSectionStatus.Unavailable, "This policy client cannot bind to the selected environment."));

    Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
        string rule,
        string mode,
        string reason,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeForEnvironmentAsync(
        ConsoleEnvironmentProfile profile,
        string rule,
        string mode,
        string reason,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OperateSectionResult<OpsAutonomyPolicyResponse>.Denied(
            OperateSectionStatus.Unavailable, "This policy client cannot bind to the selected environment."));

    Task<OperateSectionResult<OpsAutonomySettingsResponse>> SetKillSwitchAsync(
        bool enabled,
        string reason,
        CancellationToken cancellationToken = default);
}
