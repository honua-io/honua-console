namespace Honua.Console.Shell.Models;

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

public sealed record ConsoleSetTimeInfoResult : ConsoleOperationResult<ConsoleSetTimeInfoResult>
{
    public string? StartTimeField { get; init; }

    public string? EndTimeField { get; init; }

    public string? TrackIdField { get; init; }
}
