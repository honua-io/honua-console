using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live implementation of the service time-info operation. Reads a service's time-info from its settings and
/// sets it on honua-server through <see cref="IHonuaAdminOperateClient"/> and maps the result (or rejection).
/// It never fabricates success — every result reflects what the server read back.
/// </summary>
public sealed class HonuaServerConsoleTimeInfoOperation : IConsoleTimeInfoOperation
{
    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerConsoleTimeInfoOperation(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ConsoleServiceTimeInfo> GetTimeInfoAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var result = await _client.GetServiceSettingsAsync(serviceName, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } settings)
        {
            return new ConsoleServiceTimeInfo
            {
                Bound = true,
                ServiceName = settings.ServiceName ?? serviceName,
                StartTimeField = settings.TimeInfo?.StartTimeField,
                EndTimeField = settings.TimeInfo?.EndTimeField,
                TrackIdField = settings.TimeInfo?.TrackIdField,
            };
        }

        return ConsoleServiceTimeInfo.Unbound(
            serviceName,
            result.Issue?.Detail ?? "The Honua server did not return settings for this service.");
    }

    public async Task<ConsoleSetTimeInfoResult> SetTimeInfoAsync(
        string serviceName,
        string? startTimeField,
        string? endTimeField,
        string? trackIdField,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var request = new HonuaAdminUpdateTimeInfoRequest
        {
            StartTimeField = Normalize(startTimeField),
            EndTimeField = Normalize(endTimeField),
            TrackIdField = Normalize(trackIdField),
        };

        var result = await _client.UpdateServiceTimeInfoAsync(serviceName, request, cancellationToken).ConfigureAwait(false);
        if (result.Data is { } settings)
        {
            return new ConsoleSetTimeInfoResult
            {
                Succeeded = true,
                State = "Updated",
                Detail = "The service's time-info was updated on honua-server.",
                StartTimeField = settings.TimeInfo?.StartTimeField,
                EndTimeField = settings.TimeInfo?.EndTimeField,
                TrackIdField = settings.TimeInfo?.TrackIdField,
            };
        }

        var issue = result.Issue;
        return new ConsoleSetTimeInfoResult
        {
            Succeeded = false,
            State = issue?.State ?? "Unavailable",
            Detail = issue?.Detail ?? "The Honua server did not accept the time-info update.",
        };
    }

    // Blank/whitespace inputs clear the field server-side (sent as null).
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
