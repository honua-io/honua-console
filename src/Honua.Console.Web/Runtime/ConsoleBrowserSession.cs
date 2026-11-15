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
        liveAnnouncement = IsAuthenticated;
        Changed?.Invoke(CreateSnapshot());
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
    }
}

public sealed record ConsoleSessionCredentials(
    string? CsrfToken = null,
    string? AccessToken = null,
    DateTimeOffset? ExpiresAt = null);
