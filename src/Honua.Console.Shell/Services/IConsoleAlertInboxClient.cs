using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleAlertInboxClient
{
    Task<OperateSectionResult<ConsoleAlertInboxSnapshot>> ListAlertsAsync(
        CancellationToken cancellationToken = default, string? cursor = null, ConsoleAlertAuthority? expectedAuthority = null);

    Task<OperateSectionResult<ObservabilityAlertEventResponse>> ChangeAlertLifecycleAsync(
        ConsoleAlertLifecycleCommand command, CancellationToken cancellationToken = default);

    Task<OperateSectionResult<ObservabilityAlertEventResponse>> AcknowledgeAlertAsync(
        ConsoleAlertAcknowledgement acknowledgement, CancellationToken cancellationToken = default);
}

public sealed record ConsoleAlertAuthority(
    string ProfileId, Uri ServerBaseUri, string TenantId, ConsoleAccountAuthMode AuthMode,
    string AccountId, string AccountTenantId, string SessionAccountId, string SessionTenantId, Uri? SessionServerBaseUri)
{
    public static ConsoleAlertAuthority Capture(ConsoleEnvironmentProfile profile, ConsoleAccountSession? session) =>
        new(profile.Id, profile.ServerBaseUri, profile.TenantId, profile.Account.AuthMode,
            profile.Account.AccountId, profile.Account.TenantId, session?.AccountId ?? string.Empty,
            session?.TenantId ?? string.Empty, session?.ServerBaseUri);
}

public sealed record ConsoleAlertInboxSnapshot(ConsoleAlertAuthority Authority, ObservabilityAlertEventPageResponse Page);

public sealed record ConsoleAlertAcknowledgement(long EventId, ConsoleAlertAuthority Authority, string RequestId);

public enum ConsoleAlertLifecycleAction { Snooze, Resolve }

public sealed record ConsoleAlertLifecycleCommand(long EventId, ConsoleAlertAuthority Authority,
    string RequestId, ConsoleAlertLifecycleAction Action, DateTimeOffset? SuppressUntil = null)
{
    // PostgreSQL alert timestamps retain microseconds rather than 100ns ticks.
    public static DateTimeOffset NormalizeDeadline(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);
}
