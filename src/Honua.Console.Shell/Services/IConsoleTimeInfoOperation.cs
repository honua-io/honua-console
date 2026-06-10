using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The console's service time-info operation: reads a service's temporal time fields and sets them on
/// honua-server (<c>GET /api/v1/admin/services/{svc}/settings</c> + <c>PUT .../timeinfo</c>). The live
/// implementation is DI-gated on a configured server base URL; otherwise the surface binds to
/// <see cref="UnsupportedConsoleTimeInfoOperation"/> (missing-binding, no network call). It never fabricates
/// time fields (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleTimeInfoOperation
{
    /// <summary>Reads the service's current time-info (start/end/track fields) from the server settings.</summary>
    Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(string serviceName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the service's time-info. Blank/whitespace fields are sent as null, clearing them server-side.
    /// </summary>
    Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
        string serviceName,
        string? startTimeField,
        string? endTimeField,
        string? trackIdField,
        CancellationToken cancellationToken = default);
}
