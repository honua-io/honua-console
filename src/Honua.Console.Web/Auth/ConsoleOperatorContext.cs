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
/// <item>the authenticated <see cref="HttpContext.User"/> — authoritative for the request pipeline
/// (e.g. the admin-keyed map-proxy endpoints, the cookie/edge sign-in request); then</item>
/// <item>the <b>circuit's</b> ambient operator key, established from the circuit
/// <c>AuthenticationStateProvider</c> by <see cref="CircuitOperatorContextHandler"/> for the lifetime of
/// every inbound circuit activity. Blazor Server interactive components run on the circuit with NO active
/// <see cref="HttpContext"/> (the initial HTTP request is long gone), so circuit-time outbound calls made
/// through the singleton honua-server clients (which also have no <see cref="HttpContext"/>) MUST resolve
/// the operator from the circuit auth state — not HttpContext (honua-console#256).</item>
/// </list>
/// When neither is present the key is <see cref="AnonymousKey"/> — anonymous public surfaces never carry
/// an operator bearer, so they share one harmless partition. For server-bound calls that act with
/// honua-server privileges, callers MUST instead use <see cref="RequireOperatorKey"/>, which fails closed
/// rather than silently keying an authenticated operator's request into the shared anonymous partition.
/// </summary>
public interface IConsoleOperatorContext
{
    /// <summary>Stable partition key for the operator the current context is acting as.</summary>
    string CurrentOperatorKey { get; }

    /// <summary>
    /// <see langword="true"/> when the current context resolves to an authenticated operator (i.e.
    /// <see cref="CurrentOperatorKey"/> is not the shared <see cref="ConsoleOperatorContext.AnonymousKey"/>).
    /// </summary>
    bool HasOperator { get; }

    /// <summary>
    /// Returns the authenticated operator partition key, or throws when the current context cannot resolve
    /// one. Use this on any path that acts with honua-server privileges (the binding handler / map-proxy
    /// bearer resolution) so an authenticated operator's request can NEVER silently fall back to the shared
    /// anonymous partition — a cross-operator bleed (honua-console#256). Genuinely-anonymous public
    /// surfaces never call this; they read <see cref="CurrentOperatorKey"/> directly.
    /// </summary>
    /// <exception cref="ConsoleOperatorContextUnresolvedException">
    /// The current execution context has no authenticated operator (no <see cref="HttpContext.User"/> and
    /// no circuit operator ambient).
    /// </exception>
    string RequireOperatorKey();
}

/// <summary>
/// Thrown when a server-bound call (one that would act with honua-server privileges) cannot resolve an
/// authenticated operator partition key. Failing closed here prevents an authenticated operator's request
/// from collapsing into the shared anonymous partition and bleeding across operators (honua-console#256).
/// </summary>
public sealed class ConsoleOperatorContextUnresolvedException : InvalidOperationException
{
    public ConsoleOperatorContextUnresolvedException()
        : base("No authenticated operator is in scope for this server-bound call. The operator identity "
            + "could not be resolved from the request (HttpContext.User) or the interactive circuit's "
            + "authentication state, so the call is refused rather than partitioned into the shared "
            + "anonymous store (honua-console#256).")
    {
    }
}

public sealed class ConsoleOperatorContext : IConsoleOperatorContext
{
    /// <summary>Partition key used when no authenticated operator is in scope (public surfaces).</summary>
    public const string AnonymousKey = "__anonymous__";

    // Circuit-bound ambient operator key. Set by CircuitOperatorContextHandler for the duration of each
    // inbound circuit activity so it flows (via the execution context) into the singleton stores /
    // binding handler invoked by component code. NOT set on the sign-in HTTP thread — that thread has an
    // HttpContext and resolves from HttpContext.User; its execution context does not flow to the circuit.
    private static readonly AsyncLocal<string?> CircuitOperator = new();

    private readonly IHttpContextAccessor _httpContextAccessor;

    public ConsoleOperatorContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public string CurrentOperatorKey => ResolveCurrent() ?? AnonymousKey;

    public bool HasOperator => ResolveCurrent() is not null;

    public string RequireOperatorKey() => ResolveCurrent() ?? throw new ConsoleOperatorContextUnresolvedException();

    private string? ResolveCurrent()
    {
        // 1) The request pipeline (map-proxy, sign-in) carries the authenticated operator on HttpContext.User.
        var fromRequest = ResolveKey(_httpContextAccessor.HttpContext?.User);
        if (fromRequest is not null)
        {
            return fromRequest;
        }

        // 2) Interactive circuit: no HttpContext. The circuit handler established the operator key from the
        //    circuit's AuthenticationStateProvider for this activity's execution context.
        return CircuitOperator.Value;
    }

    /// <summary>
    /// Sets the circuit-bound ambient operator key for the current execution flow (the inbound circuit
    /// activity). Called by <see cref="CircuitOperatorContextHandler"/>; <see langword="null"/> clears it.
    /// </summary>
    internal static void SetCircuitOperatorKey(string? operatorKey) => CircuitOperator.Value = operatorKey;

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
