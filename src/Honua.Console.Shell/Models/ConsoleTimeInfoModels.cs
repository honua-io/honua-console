namespace Honua.Console.Shell.Models;

/// <summary>
/// A service's temporal time-info (start/end time fields + track id field) as read from honua-server.
/// <see cref="Bound"/> is false (with <see cref="Detail"/>) when no server is configured or the settings
/// could not be read.
/// </summary>
public sealed record ConsoleServiceTimeInfo
{
    public bool Bound { get; init; }

    public string? Detail { get; init; }

    public string? ServiceName { get; init; }

    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }

    public static ConsoleServiceTimeInfo Unbound(string serviceName, string detail) =>
        new() { Bound = false, ServiceName = serviceName, Detail = detail };
}

/// <summary>Outcome of setting a service's time-info.</summary>
public sealed record ConsoleSetTimeInfoResult : ConsoleOperationResult<ConsoleSetTimeInfoResult>
{
    /// <summary>The service's time-info as re-read by the server after the change (when it succeeded).</summary>
    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }
}
