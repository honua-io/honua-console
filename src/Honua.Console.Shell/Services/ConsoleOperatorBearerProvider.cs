using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Describes the result of exchanging an authenticated honua-server admin
/// session for a short-lived, forwardable operator bearer.
/// </summary>
public enum ConsoleOperatorBearerExchangeStatus
{
    /// <summary>A bearer was issued.</summary>
    Issued,

    /// <summary>The server session is missing, expired, or not authorized.</summary>
    Denied,

    /// <summary>The exchange topology or server capability is unavailable.</summary>
    Unavailable
}

/// <summary>
/// Result returned by <see cref="IConsoleOperatorBearerExchange"/>.
/// </summary>
public sealed record ConsoleOperatorBearerExchangeResult
{
    /// <summary>Gets the exchange outcome.</summary>
    public required ConsoleOperatorBearerExchangeStatus Status { get; init; }

    /// <summary>Gets the issued bearer, when successful.</summary>
    public string AccessToken { get; init; } = string.Empty;

    /// <summary>Gets the issued bearer's absolute expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Gets a safe operator-facing explanation.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Creates a successful issuance result.</summary>
    public static ConsoleOperatorBearerExchangeResult Issued(string accessToken, DateTimeOffset expiresAt) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Issued,
        AccessToken = accessToken,
        ExpiresAt = expiresAt
    };

    /// <summary>Creates a denied exchange result.</summary>
    public static ConsoleOperatorBearerExchangeResult Denied(string message) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Denied,
        Message = message
    };

    /// <summary>Creates an unavailable exchange result.</summary>
    public static ConsoleOperatorBearerExchangeResult Unavailable(string message) => new()
    {
        Status = ConsoleOperatorBearerExchangeStatus.Unavailable,
        Message = message
    };
}

/// <summary>
/// Exchanges the authenticated server-side admin session for the bearer issued by
/// <c>POST /api/v1/admin/auth/bearer</c>. Implementations must not use an admin API
/// key or a caller-supplied actor header for this exchange.
/// </summary>
public interface IConsoleOperatorBearerExchange
{
    /// <summary>Requests a bearer for <paramref name="profile"/>.</summary>
    Task<ConsoleOperatorBearerExchangeResult> ExchangeAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A resolved operator credential plus enough session state to distinguish an
/// interactive human from a genuinely headless caller.
/// </summary>
public sealed record ConsoleOperatorBearerResolution
{
    /// <summary>Gets the forwardable bearer, when one is available.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Gets the bearer expiry, when known.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets whether an interactive account session exists for the active profile.
    /// A sentinel or expired bearer still counts as an interactive session and must
    /// never silently downgrade to a process-wide API key.
    /// </summary>
    public bool HasInteractiveSession { get; init; }

    /// <summary>Gets a safe operator-facing explanation when no bearer is available.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Gets whether a forwardable bearer is available.</summary>
    public bool IsAvailable => !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>
/// Resolves and refreshes profile-partitioned honua-server operator bearers.
/// </summary>
public interface IConsoleOperatorBearerProvider
{
    /// <summary>Resolves a valid bearer for the active environment profile.</summary>
    Task<ConsoleOperatorBearerResolution> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Uses the protected account-session store for profile-partitioned bearer state and
/// refreshes missing/expired credentials through the configured exchange seam.
/// </summary>
public sealed class ConsoleOperatorBearerProvider : IConsoleOperatorBearerProvider
{
    private static readonly TimeSpan ExpirySafetyWindow = TimeSpan.FromSeconds(30);

    private readonly IConsoleAccountSessionStore _sessions;
    private readonly IConsoleOperatorBearerExchange _exchange;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new provider.</summary>
    public ConsoleOperatorBearerProvider(
        IConsoleAccountSessionStore sessions,
        IConsoleOperatorBearerExchange exchange,
        TimeProvider timeProvider)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<ConsoleOperatorBearerResolution> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var session = await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return Missing(session is not null, profile, "The active profile is anonymous.");
        }

        if (session is null)
        {
            return Missing(hasInteractiveSession: false, profile, detail: null);
        }

        var now = _timeProvider.GetUtcNow();
        if (IsForwardable(session.AccessToken)
            && (session.AccessTokenExpiresAt is null
                || session.AccessTokenExpiresAt > now.Add(ExpirySafetyWindow)))
        {
            return new ConsoleOperatorBearerResolution
            {
                AccessToken = session.AccessToken,
                ExpiresAt = session.AccessTokenExpiresAt,
                HasInteractiveSession = true
            };
        }

        var exchange = await _exchange.ExchangeAsync(profile, cancellationToken).ConfigureAwait(false);
        if (exchange.Status == ConsoleOperatorBearerExchangeStatus.Issued
            && IsForwardable(exchange.AccessToken)
            && exchange.ExpiresAt is { } expiresAt
            && expiresAt > now.Add(ExpirySafetyWindow))
        {
            var refreshed = session with
            {
                AccessToken = exchange.AccessToken,
                AccessTokenExpiresAt = expiresAt
            };
            await _sessions.SaveSessionAsync(refreshed, cancellationToken).ConfigureAwait(false);

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
                + "Sign in to honua-server again before retrying this human mutation. "
                + $"The Console did not use the shared admin API key.{suffix}"
        };
    }
}

/// <summary>
/// Fail-closed default used until the host configures a same-origin or trusted-edge
/// server-session bridge for the bearer exchange.
/// </summary>
public sealed class UnavailableConsoleOperatorBearerExchange : IConsoleOperatorBearerExchange
{
    /// <inheritdoc />
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
