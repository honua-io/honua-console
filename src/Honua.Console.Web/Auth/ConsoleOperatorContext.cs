using System.Security.Claims;
using System.Threading;
using Honua.Console.Shell.Security;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

// RequireOperatorKey throws when no operator is resolved so a privileged call cannot use the shared anonymous partition.
public interface IConsoleOperatorContext
{
    string CurrentOperatorKey { get; }

    bool HasOperator { get; }

    string RequireOperatorKey();
}

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
    // Shared by public surfaces that carry no operator bearer.
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

    internal static void SetCircuitOperatorKey(string? operatorKey) => CircuitOperator.Value = operatorKey;

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
