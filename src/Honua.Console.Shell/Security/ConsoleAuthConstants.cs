namespace Honua.Console.Shell.Security;

public static class ConsoleAuthConstants
{
    public const string CookieScheme = "ConsoleOperatorCookie";

    public const string OperatorBearerClaim = "honua:operator_bearer";

    public const string SessionSentinelPrefix = "profile-session:";

    public static bool IsSessionSentinel(string? token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.StartsWith(SessionSentinelPrefix, StringComparison.Ordinal);

    public const string OperatorTenantClaim = "honua:operator_tenant";

    // Configuration keys (bound from IConfiguration in the Web host).

    public const string AuthModeKey = "Honua:Console:Auth:Mode";

    public const string EdgeEnabledKey = "Honua:Console:Auth:EdgeForwarded:Enabled";

    public const string EdgeUserHeaderKey = "Honua:Console:Auth:EdgeForwarded:UserHeader";

    public const string EdgeEmailHeaderKey = "Honua:Console:Auth:EdgeForwarded:EmailHeader";

    public const string EdgeAccessTokenHeaderKey = "Honua:Console:Auth:EdgeForwarded:AccessTokenHeader";

    // When set, forwarded identity headers count only if the proxy presents this secret.
    public const string EdgeSharedSecretKey = "Honua:Console:Auth:EdgeForwarded:SharedSecret";

    public const string EdgeServerSessionIssuerKey = "Honua:Console:Auth:EdgeForwarded:ServerSessionIssuer";

    // Default header names matching oauth2-proxy / common ingress conventions.
    public const string DefaultUserHeader = "X-Forwarded-User";
    public const string DefaultEmailHeader = "X-Forwarded-Email";
    public const string DefaultAccessTokenHeader = "X-Forwarded-Access-Token";
    public const string EdgeTrustHeader = "X-Honua-Edge-Auth";
}
