using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsAutonomyClient
{
    Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
        string rule,
        string mode,
        string reason,
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsAutonomySettingsResponse>> SetKillSwitchAsync(
        bool enabled,
        string reason,
        CancellationToken cancellationToken = default);
}
