using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleTimeInfoOperation
{
    Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(string serviceName, CancellationToken cancellationToken = default);

    Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
        string serviceName,
        string? startTimeField,
        string? endTimeField,
        string? trackIdField,
        CancellationToken cancellationToken = default);
}
