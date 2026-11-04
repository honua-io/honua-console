using System.Security.Claims;
using Honua.Console.Shell.Security;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web.Auth;

public sealed class ConsoleEdgeAuthOptions
{
    public bool Enabled { get; set; }

    public string UserHeader { get; set; } = ConsoleAuthConstants.DefaultUserHeader;

    public string EmailHeader { get; set; } = ConsoleAuthConstants.DefaultEmailHeader;

    public string AccessTokenHeader { get; set; } = ConsoleAuthConstants.DefaultAccessTokenHeader;

    // When set, X-Honua-Edge-Auth must match or a direct caller can spoof an operator; when unset, the proxy must be the only path and must strip client headers.
    public string? SharedSecret { get; set; }

    // When set, the user header must be the raw OIDC subject, and issuance and refresh require that subject on this HTTPS issuer; when unset, environment identities stay independent.
    public string? ServerSessionIssuer { get; set; }

    internal static bool IsValidServerSessionIssuer(ConsoleEdgeAuthOptions options) =>
        string.IsNullOrEmpty(options.ServerSessionIssuer)
        || (options.ServerSessionIssuer == options.ServerSessionIssuer.Trim()
            && Uri.TryCreate(options.ServerSessionIssuer, UriKind.Absolute, out var issuer)
            && issuer.IsWellFormedOriginalString()
            && issuer.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(issuer.Host)
            && string.IsNullOrEmpty(issuer.UserInfo)
            && !options.ServerSessionIssuer.Contains('?')
            && !options.ServerSessionIssuer.Contains('#'));
}

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
        options.ServerSessionIssuer = _configuration[ConsoleAuthConstants.EdgeServerSessionIssuerKey];
    }

    private static string Trimmed(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

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
        var bindServerIdentity = !string.IsNullOrEmpty(_options.ServerSessionIssuer);
        if (_options.Enabled && (bindServerIdentity || context.User?.Identity?.IsAuthenticated != true))
        {
            var principal = TryBuildEdgePrincipal(context);
            if (bindServerIdentity)
            {
                // Hosted binding requires the current edge assertion, including on a stale cookie request.
                context.User = principal ?? new ClaimsPrincipal(new ClaimsIdentity());
            }

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

        var userValues = context.Request.Headers[_options.UserHeader];
        if (!string.IsNullOrEmpty(_options.ServerSessionIssuer)
            && (userValues.Count != 1 || string.IsNullOrWhiteSpace(userValues[0])))
        {
            return null;
        }

        var user = userValues.ToString();
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
