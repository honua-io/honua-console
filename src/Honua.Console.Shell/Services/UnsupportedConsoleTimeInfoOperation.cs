using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class UnsupportedConsoleTimeInfoOperation : IConsoleTimeInfoOperation
{
    private const string BindingDetail =
        "Configure Honua:Server:BaseUrl or HONUA_SERVER_BASE_URL so the console can read and set the service's time-info on honua-server.";

    public Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleServiceTimeInfo.Unbound(serviceName, BindingDetail));

    public Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
        string serviceName,
        string? startTimeField,
        string? endTimeField,
        string? trackIdField,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ConsoleSetTimeInfoResult.MissingBinding(BindingDetail));
}
