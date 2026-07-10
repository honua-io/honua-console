namespace Honua.Console.Shell.Security;

/// <summary>
/// Shared constants for the Console operator-authentication model (honua-console#233/#234).
///
/// The Console authenticates an operator at the host edge (cookie session, trusted
/// edge-forwarded identity, or — in Development — a documented dev login) and then forwards
/// that operator's identity to honua-server. The forwarded credential is the operator's
/// bearer token when one is available (e.g. an oauth2-proxy <c>X-Forwarded-Access-Token</c>
/// or honua-server's short-lived operator bearer), falling back to the configured shared admin key
/// only when no operator bearer exists. Per-principal RBAC on honua-server is therefore
/// honoured whenever a real operator bearer is present.
/// </summary>
public static class ConsoleAuthConstants
{
    /// <summary>Cookie authentication scheme used by the Console host.</summary>
    public const string CookieScheme = "ConsoleOperatorCookie";

    /// <summary>Claim carrying the operator's bearer token to forward to honua-server (when present).</summary>
    public const string OperatorBearerClaim = "honua:operator_bearer";

    /// <summary>
    /// Prefix of the placeholder access token written into the account session when an operator is
    /// authenticated to the Console but no forwardable honua-server bearer exists yet (dev login, or
    /// an edge proxy that does not pass an access token). It marks the session as "signed in" for
    /// client-side read context, but is never forwarded to honua-server — the shared admin-key
    /// fallback applies instead. A real operator bearer never carries this prefix.
    /// </summary>
    public const string SessionSentinelPrefix = "profile-session:";

    /// <summary>True when <paramref name="token"/> is a non-forwardable Console session sentinel.</summary>
    public static bool IsSessionSentinel(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.StartsWith(SessionSentinelPrefix, StringComparison.Ordinal);

    /// <summary>Claim carrying the operator's tenant, when the edge/IdP supplies one.</summary>
    public const string OperatorTenantClaim = "honua:operator_tenant";

    // Configuration keys (bound from IConfiguration in the Web host).

    /// <summary>Auth mode: <c>EdgeForwarded</c>, <c>Dev</c>, or unset (fail-closed in non-Development).</summary>
    public const string AuthModeKey = "Honua:Console:Auth:Mode";

    /// <summary>When true, trust the edge-forwarded identity headers below.</summary>
    public const string EdgeEnabledKey = "Honua:Console:Auth:EdgeForwarded:Enabled";

    /// <summary>Header carrying the operator's stable id/subject (e.g. <c>X-Forwarded-User</c>).</summary>
    public const string EdgeUserHeaderKey = "Honua:Console:Auth:EdgeForwarded:UserHeader";

    /// <summary>Header carrying the operator's email/display (e.g. <c>X-Forwarded-Email</c>).</summary>
    public const string EdgeEmailHeaderKey = "Honua:Console:Auth:EdgeForwarded:EmailHeader";

    /// <summary>Header carrying the operator's forwardable bearer (e.g. <c>X-Forwarded-Access-Token</c>).</summary>
    public const string EdgeAccessTokenHeaderKey = "Honua:Console:Auth:EdgeForwarded:AccessTokenHeader";

    /// <summary>
    /// Optional shared secret the trusted proxy must present (header <c>X-Honua-Edge-Auth</c>) so the
    /// Console never accepts forwarded-identity headers from an untrusted direct caller.
    /// </summary>
    public const string EdgeSharedSecretKey = "Honua:Console:Auth:EdgeForwarded:SharedSecret";

    // Default header names matching oauth2-proxy / common ingress conventions.
    public const string DefaultUserHeader = "X-Forwarded-User";
    public const string DefaultEmailHeader = "X-Forwarded-Email";
    public const string DefaultAccessTokenHeader = "X-Forwarded-Access-Token";
    public const string EdgeTrustHeader = "X-Honua-Edge-Auth";
}
