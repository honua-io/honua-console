using System.Security.Claims;
using System.Threading;
using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Resolves the stable partition key for the operator the current execution context is acting as
/// (honua-console#233 S1 hardening). The browser Console host is multi-operator: many operators are
/// served by ONE process, so any process-wide operator state (active environment profile, account
/// binding, forwarded bearer) MUST be partitioned by operator or one operator's identity/bearer bleeds
/// into another's requests.
///
/// The key is resolved from, in order:
/// <list type="number">
/// <item>the authenticated <see cref="HttpContext.User"/> (authoritative for the request pipeline,
/// e.g. the admin-keyed map-proxy endpoints); then</item>
/// <item>an <see cref="AsyncLocal{T}"/> ambient set by <see cref="ConsoleOperatorSessionBridge"/> at
/// sign-in and re-established per circuit, so circuit-time outbound calls made through the singleton
/// honua-server clients (which have no <see cref="HttpContext"/>) still partition by operator.</item>
/// </list>
/// When neither is present the key is <see cref="AnonymousKey"/> — anonymous public surfaces never
/// carry an operator bearer, so they share one harmless partition.
/// </summary>
public interface IConsoleOperatorContext
{
    /// <summary>Stable partition key for the operator the current context is acting as.</summary>
    string CurrentOperatorKey { get; }
}

public sealed class ConsoleOperatorContext : IConsoleOperatorContext
{
    /// <summary>Partition key used when no authenticated operator is in scope (public surfaces).</summary>
    public const string AnonymousKey = "__anonymous__";

    private static readonly AsyncLocal<string?> Ambient = new();

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ConsoleOperatorContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string CurrentOperatorKey
    {
        get
        {
            var fromRequest = ResolveKey(_httpContextAccessor.HttpContext?.User);
            if (fromRequest is not null)
            {
                return fromRequest;
            }

            return Ambient.Value ?? AnonymousKey;
        }
    }

    /// <summary>
    /// Establishes the ambient operator key for the remainder of the current async flow (and the flows
    /// it spawns). Called by the session bridge so circuit-time work bound to the same logical context
    /// partitions to the signed-in operator even without an <see cref="HttpContext"/>.
    /// </summary>
    internal static void SetAmbient(ClaimsPrincipal? principal) => Ambient.Value = ResolveKey(principal);

    /// <summary>Derives a stable per-operator key from a principal, or <c>null</c> when unauthenticated.</summary>
    public static string? ResolveKey(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        var name = principal.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
