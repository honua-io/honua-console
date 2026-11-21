using System.Security.Claims;

namespace Honua.Console.Web.Runtime;

// One WASM app instance is one browser tab. Tokens stay in memory, never localStorage or config.json.
public sealed class ConsoleBrowserSession
{
    private readonly Guid _authorityId = Guid.NewGuid();

    internal string? CaptureAuthority(Uri apiBaseUri) => IsAuthenticated
        ? $"{apiBaseUri.AbsoluteUri}|{_authorityId:N}|{Revision}"
        : null;

    public long Revision { get; private set; }
    public ClaimsPrincipal Principal { get; private set; } = new(new ClaimsIdentity());
    public string? AccessToken { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? CsrfToken { get; private set; }

    // Bearer for a separate manager. It is not the serving session and is not published to script.
    internal string? ManagementAccessToken { get; private set; }
    internal DateTimeOffset? ManagementExpiresAt { get; private set; }

    // Why sign-in got no manager bearer; management calls report it instead of asking for another sign-in.
    internal string? ManagementUnavailableReason { get; private set; }
    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true
        && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    public event Action<ConsoleBrowserSessionSnapshot>? Changed;

    private bool liveAnnouncement;

    public ConsoleBrowserSessionSnapshot Snapshot()
    {
        if (liveAnnouncement && !IsAuthenticated)
        {
            liveAnnouncement = false;
            AccessToken = null;
            ManagementAccessToken = null;
            ManagementExpiresAt = null;
            Revision++;
            var expired = CreateSnapshot();
            Changed?.Invoke(expired);
            return expired;
        }
        return CreateSnapshot();
    }

    public void Set(ClaimsPrincipal principal, ConsoleSessionCredentials? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(principal);
        Revision++;
        Principal = principal;
        CsrfToken = credentials?.CsrfToken;
        AccessToken = credentials?.AccessToken;
        ExpiresAt = credentials?.ExpiresAt;
        ManagementAccessToken = null;
        ManagementExpiresAt = null;
        ManagementUnavailableReason = null;
        liveAnnouncement = IsAuthenticated;
        Changed?.Invoke(CreateSnapshot());
    }

    internal void ReplaceServingCredentials(ClaimsPrincipal principal, ConsoleSessionCredentials? credentials)
    {
        var managementToken = ManagementAccessToken;
        var managementExpiry = ManagementExpiresAt;
        var managementUnavailable = ManagementUnavailableReason;
        Set(principal, credentials);
        if (!string.IsNullOrWhiteSpace(managementToken)) RememberManagementBearer(managementToken, managementExpiry);
        ManagementUnavailableReason = managementUnavailable;
    }

    private ConsoleBrowserSessionSnapshot CreateSnapshot() => new(
        Revision,
        IsAuthenticated,
        Principal.AccountId(),
        IsAuthenticated ? AccessToken : null,
        IsAuthenticated ? CsrfToken : null,
        ExpiresAt);

    public void Clear() => Set(new ClaimsPrincipal(new ClaimsIdentity()));

    internal void RememberManagementBearer(string token, DateTimeOffset? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ManagementAccessToken = token;
        ManagementExpiresAt = expiresAt;
        ManagementUnavailableReason = null;
    }

    internal void RememberManagementUnavailable(System.Net.HttpStatusCode? status)
    {
        ManagementAccessToken = null;
        ManagementExpiresAt = null;
        ManagementUnavailableReason = status == System.Net.HttpStatusCode.TooManyRequests
            ? "The serving API is limiting sign-in attempts, so it issued no bearer for the management endpoint (HTTP 429). Wait a few minutes, then sign in again."
            : status is { } code
            ? $"The serving API did not issue a bearer for the management endpoint (HTTP {(int)code}). Check its operator bearer and allowed origin settings, then sign in again."
            : "The serving API did not issue a bearer for the management endpoint. Check its operator bearer and allowed origin settings, then sign in again.";
    }
}

public sealed record ConsoleSessionCredentials(
    string? CsrfToken = null,
    string? AccessToken = null,
    DateTimeOffset? ExpiresAt = null);
