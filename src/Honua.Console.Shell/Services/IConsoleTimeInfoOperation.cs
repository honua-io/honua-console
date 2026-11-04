using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleTimeInfoOperation
{
    Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(string serviceName, CancellationToken cancellationToken = default);

    // Blank or whitespace fields are sent as null and clear the value on the server.
    Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
        string serviceName,
        string? startTimeField,
        string? endTimeField,
        string? trackIdField,
        CancellationToken cancellationToken = default);
}
