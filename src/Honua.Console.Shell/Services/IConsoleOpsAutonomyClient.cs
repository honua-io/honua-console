using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsAutonomyClient
{
    // A client with an active profile must bind its requests to the supplied profile.
    IConsoleOpsAutonomyClient ForEnvironment(ConsoleEnvironmentProfile profile) => this;

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
