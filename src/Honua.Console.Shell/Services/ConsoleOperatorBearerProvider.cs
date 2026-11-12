using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;

namespace Honua.Console.Shell.Services;

public enum ConsoleOperatorBearerExchangeStatus
{
    Issued,

    Denied,

    Unavailable
}

public sealed record ConsoleOperatorBearerExchangeResult
{
    public required ConsoleOperatorBearerExchangeStatus Status { get; init; }

    public string AccessToken { get; init; } = string.Empty;

    public DateTimeOffset? ExpiresAt { get; init; }

    public string Message { get; init; } = string.Empty;

    public static ConsoleOperatorBearerExchangeResult Issued(string accessToken, DateTimeOffset expiresAt) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Issued,
        AccessToken = accessToken,
        ExpiresAt = expiresAt
    };

    public static ConsoleOperatorBearerExchangeResult Denied(string message) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Denied,
        Message = message
    };

    public static ConsoleOperatorBearerExchangeResult Unavailable(string message) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Unavailable,
        Message = message
    };
}

// Implementations must not send an admin API key or a caller-supplied actor header on the bearer exchange.
public interface IConsoleOperatorBearerExchange
{
    Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

// Holds authentication serialization until the refreshed session is committed.
public interface IConsoleOperatorBearerExchangeLease : IConsoleOperatorBearerExchange, IDisposable
{
}

// The exchange scope also protects account-session reads and writes.
public interface IConsoleOperatorBearerExchangeLeaseProvider
{
    Task<IConsoleOperatorBearerExchangeLease> EnterSessionAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed record ConsoleOperatorBearerResolution
{
    public string? AccessToken { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    // A sentinel or expired bearer still counts as an interactive session and must not fall back to the process-wide API key.
    public bool HasInteractiveSession { get; init; }

    public string Message { get; init; } = string.Empty;

    // The host transport (the static Console's browser fetch) attaches the signed-in operator's cookie or bearer itself.
    public bool UsesBrowserTransport { get; init; }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(AccessToken);
}

public interface IConsoleOperatorBearerProvider
{
    Task<ConsoleOperatorBearerResolution> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

public sealed class ConsoleOperatorBearerProvider : IConsoleOperatorBearerProvider
{
    private static readonly TimeSpan ExpirySafetyWindow = TimeSpan.FromSeconds(30);

    private readonly IConsoleAccountSessionStore _sessions;
    private readonly IConsoleOperatorBearerExchange _exchange;
    private readonly TimeProvider _timeProvider;

    public ConsoleOperatorBearerProvider(
        IConsoleAccountSessionStore sessions,
        IConsoleOperatorBearerExchange exchange,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ConsoleOperatorBearerResolution> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var authentication = _exchange is IConsoleOperatorBearerExchangeLeaseProvider leases
            ? await leases.EnterSessionAsync(profile, cancellationToken).ConfigureAwait(false)
            : null;
        var scopedExchange = authentication ?? _exchange;

        var snapshots = _sessions as IConsoleAccountSessionSnapshotStore;
        var snapshot = snapshots is null ? null
            : await snapshots.CaptureSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var session = snapshot is not null ? snapshot.Session
            : await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return Missing(session is not null, profile, "The active profile is anonymous.");
        }

        if (session is null)
        {
            return Missing(hasInteractiveSession: false, profile, detail: null);
        }

        if (session.ServerBaseUri is not null && session.ServerBaseUri != profile.ServerBaseUri)
        {
            return Missing(hasInteractiveSession: true, profile, "The environment target changed. Sign in again.");
        }

        var now = _timeProvider.GetUtcNow();
        if (IsForwardable(session.AccessToken)
            && (session.AccessTokenExpiresAt is null
                || session.AccessTokenExpiresAt > now.Add(ExpirySafetyWindow)))
        {
            if (snapshots is not null && snapshot is not null
                && !await snapshots.IsCurrentSessionAsync(snapshot, cancellationToken).ConfigureAwait(false))
            {
                return Missing(hasInteractiveSession: false, profile, "The operator session changed. Sign in again.");
            }

            return new ConsoleOperatorBearerResolution
            {
                AccessToken = session.AccessToken,
                ExpiresAt = session.AccessTokenExpiresAt,
                HasInteractiveSession = true
            };
        }

        var exchange = await scopedExchange.ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
        if (exchange.Status == ConsoleOperatorBearerExchangeStatus.Issued
            && IsForwardable(exchange.AccessToken)
            && exchange.ExpiresAt is { } expiresAt
            && expiresAt > now.Add(ExpirySafetyWindow))
        {
            var refreshed = session with
            {
                AccessToken = exchange.AccessToken,
                ServerBaseUri = profile.ServerBaseUri,
                AccessTokenExpiresAt = expiresAt
            };
            if (snapshots is not null && snapshot is not null)
            {
                if (!await snapshots.TrySaveSessionAsync(snapshot, refreshed, cancellationToken).ConfigureAwait(false))
                {
                    return Missing(hasInteractiveSession: false, profile, "The operator session changed. Sign in again.");
                }
            }
            else
            {
                await _sessions.SaveSessionAsync(refreshed, cancellationToken).ConfigureAwait(false);
            }

            return new ConsoleOperatorBearerResolution
            {
                AccessToken = exchange.AccessToken,
                ExpiresAt = expiresAt,
                HasInteractiveSession = true
            };
        }

        return Missing(hasInteractiveSession: true, profile, exchange.Message);
    }

    private static bool IsForwardable(string? token) =>
        !string.IsNullOrWhiteSpace(token) && !ConsoleAuthConstants.IsSessionSentinel(token);

    private static ConsoleOperatorBearerResolution Missing(
        bool hasInteractiveSession,
        ConsoleEnvironmentProfile profile,
        string? detail)
    {
        var environment = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.Id : profile.DisplayName;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail.Trim()}";
        return new ConsoleOperatorBearerResolution
        {
            HasInteractiveSession = hasInteractiveSession,
            Message = $"Your operator credential for {environment} is missing or expired. "
                + "Sign in to honua-server again before retrying this request. "
                + $"The Console did not use the shared admin API key.{suffix}"
        };
    }
}

// Fail-closed stand-in until the host configures a same-origin or trusted-edge server-session bridge.
public sealed class UnavailableConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange
{
    public Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ConsoleOperatorBearerExchangeResult.Unavailable(
            "This Console host has no trusted honua-server session bridge configured."));
    }
}
