using System.Net.Http;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Fail-closed-by-construction guard at the OUTERMOST position of the privileged Family-A server-bound
/// IHttpClientFactory handler chain (honua-console#254). It refuses any outbound call that is not made on
/// behalf of a resolved operator, BEFORE the inner <c>HonuaServerBindingHandler</c> retargets the request
/// or attaches a credential — so an unresolved operator can never act with honua-server privileges (nor
/// silently fall back to the shared admin key on the configured server, the recurring fail-open class this
/// issue closes). It retires the <c>__anonymous__</c> sentinel on the server-bound path: there is no
/// anonymous partition to collapse into here, the call simply throws.
///
/// WHY <see cref="IConsoleOperatorContext"/> AND NOT THE SCOPED <see cref="IConsoleOperatorScope"/>. This
/// handler runs inside the IHttpClientFactory handler chain, which is pooled and rotated on its own
/// lifetime — it is NOT resolved from the consuming circuit/request DI scope. A constructor-injected
/// <see cref="IConsoleOperatorScope"/> would therefore capture the <c>AuthenticationStateProvider</c> of
/// the handler-rotation scope, not the active circuit's, and resolve the wrong operator (or none) during
/// interactive rendering. <see cref="IConsoleOperatorContext"/> is the ambient-bridged accessor that DOES
/// resolve correctly in every execution context: it reads <c>HttpContext.User</c> on the request pipeline
/// and the circuit operator ambient that <see cref="CircuitOperatorContextHandler"/> establishes for each
/// inbound circuit activity (honua-console#256). <see cref="RequireOperatorKey"/> fails closed when neither
/// is present, which is exactly the deny this guard enforces. The request-pipeline counterpart — the
/// admin-keyed <c>/map-proxy/*</c> BFF endpoints, which DO run in the request scope — use
/// <see cref="IConsoleOperatorScope"/> directly (honua-console#257).
/// </summary>
public sealed class ConsoleServerBoundOperatorGuardHandler : DelegatingHandler
{
    private readonly IConsoleOperatorContext _operatorContext;

    public ConsoleServerBoundOperatorGuardHandler(IConsoleOperatorContext operatorContext)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Hard deny: throws ConsoleOperatorContextUnresolvedException when no authenticated operator is in
        // scope, so the inner binding handler (retarget + bearer/admin-key) is never reached.
        _ = _operatorContext.RequireOperatorKey();
        return base.SendAsync(request, cancellationToken);
    }
}
