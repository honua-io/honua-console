using System.Security.Claims;
using Honua.Console.Shell.Security;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web.Auth;

/// <summary>Bound trusted-edge configuration (<c>Honua:Console:Auth:EdgeForwarded:*</c>).</summary>
public sealed class ConsoleEdgeAuthOptions
{
    public bool Enabled { get; set; }

    public string UserHeader { get; set; } = ConsoleAuthConstants.DefaultUserHeader;

    public string EmailHeader { get; set; } = ConsoleAuthConstants.DefaultEmailHeader;

    public string AccessTokenHeader { get; set; } = ConsoleAuthConstants.DefaultAccessTokenHeader;

    /// <summary>
    /// Optional shared secret the trusted proxy must present (header <c>X-Honua-Edge-Auth</c>). When
    /// set, forwarded-identity headers are honoured only if this secret matches — so a direct caller
    /// that reaches the Console origin cannot spoof an operator identity. When unset, the deployment
    /// MUST guarantee the proxy strips client-supplied identity headers and is the only network path.
    /// </summary>
    public string? SharedSecret { get; set; }
}

/// <summary>Binds <see cref="ConsoleEdgeAuthOptions"/> from configuration.</summary>
internal sealed class ConsoleEdgeAuthOptionsSetup : IConfigureOptions<ConsoleEdgeAuthOptions>
{
    private readonly IConfiguration _configuration;

    public ConsoleEdgeAuthOptionsSetup(IConfiguration configuration) => _configuration = configuration;

    public void Configure(ConsoleEdgeAuthOptions options)
    {
        options.Enabled = _configuration.GetValue(ConsoleAuthConstants.EdgeEnabledKey, false)
            || string.Equals(_configuration[ConsoleAuthConstants.AuthModeKey], "EdgeForwarded", StringComparison.OrdinalIgnoreCase);
        options.UserHeader = Trimmed(_configuration[ConsoleAuthConstants.EdgeUserHeaderKey], ConsoleAuthConstants.DefaultUserHeader);
        options.EmailHeader = Trimmed(_configuration[ConsoleAuthConstants.EdgeEmailHeaderKey], ConsoleAuthConstants.DefaultEmailHeader);
        options.AccessTokenHeader = Trimmed(_configuration[ConsoleAuthConstants.EdgeAccessTokenHeaderKey], ConsoleAuthConstants.DefaultAccessTokenHeader);
        options.SharedSecret = _configuration[ConsoleAuthConstants.EdgeSharedSecretKey];
    }

    private static string Trimmed(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

/// <summary>
/// Establishes the operator identity from trusted edge-forwarded headers when the request is not
/// already authenticated (e.g. by the dev cookie). Runs per request so a revoked edge session cannot
/// be replayed from a stale cookie. The operator's forwarded bearer (when supplied) is captured as a
/// claim and bridged into the account session for forwarding to honua-server.
/// </summary>
public sealed class ConsoleEdgeIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConsoleEdgeAuthOptions _options;
    private readonly ConsoleOperatorSessionBridge _bridge;
    private readonly ILogger<ConsoleEdgeIdentityMiddleware> _logger;
    private bool _warnedNoSecret;

    public ConsoleEdgeIdentityMiddleware(
        RequestDelegate next,
        IOptions<ConsoleEdgeAuthOptions> options,
        ConsoleOperatorSessionBridge bridge,
        ILogger<ConsoleEdgeIdentityMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _bridge = bridge;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.Enabled && context.User?.Identity?.IsAuthenticated != true)
        {
            var principal = TryBuildEdgePrincipal(context);
            if (principal is not null)
            {
                context.User = principal;
                await _bridge.SyncAsync(principal, context.RequestAborted).ConfigureAwait(false);
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private ClaimsPrincipal? TryBuildEdgePrincipal(HttpContext context)
    {
        if (!string.IsNullOrEmpty(_options.SharedSecret))
        {
            var presented = context.Request.Headers[ConsoleAuthConstants.EdgeTrustHeader];
            if (presented.Count == 0 || !string.Equals(presented[0], _options.SharedSecret, StringComparison.Ordinal))
            {
                return null;
            }
        }
        else if (!_warnedNoSecret)
        {
            _warnedNoSecret = true;
            _logger.LogWarning(
                "Console edge-forwarded authentication is enabled without a shared secret. The trusted "
                + "proxy MUST strip client-supplied identity headers and be the only network path to this host.");
        }

        var user = context.Request.Headers[_options.UserHeader].ToString();
        var email = context.Request.Headers[_options.EmailHeader].ToString();
        if (string.IsNullOrWhiteSpace(user) && string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var subject = string.IsNullOrWhiteSpace(user) ? email : user;
        var display = string.IsNullOrWhiteSpace(email) ? user : email;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Name, display),
        };
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        var accessToken = context.Request.Headers[_options.AccessTokenHeader].ToString();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            claims.Add(new Claim(ConsoleAuthConstants.OperatorBearerClaim, accessToken));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "EdgeForwarded"));
    }
}
