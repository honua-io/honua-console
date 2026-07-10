using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Narrow client for the graduated ops-autonomy operator surface. It reads server-owned
/// settings, policies, graduation evidence and audit events, and performs only the two
/// human-attributable policy mutations supported by honua-server.
/// </summary>
public interface IConsoleOpsAutonomyClient
{
    /// <summary>Loads the server-confirmed autonomy surface.</summary>
    Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Changes one rule's mode and returns the server-confirmed policy.</summary>
    Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
        string rule,
        string mode,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Changes the global kill switch and returns the server-confirmed settings.</summary>
    Task<OperateSectionResult<OpsAutonomySettingsResponse>> SetKillSwitchAsync(
        bool enabled,
        string reason,
        CancellationToken cancellationToken = default);
}
