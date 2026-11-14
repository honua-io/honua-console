using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleAlertInboxClient
{
    Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(
        CancellationToken cancellationToken = default, string? cursor = null);

    Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
        ConsoleAlertAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}

public sealed record ConsoleAlertInboxSnapshot(string ProfileId, ObservabilityAlertEventPageResponse Page);

public sealed record ConsoleAlertAcknowledgement(long EventId, string ProfileId, string RequestId);
