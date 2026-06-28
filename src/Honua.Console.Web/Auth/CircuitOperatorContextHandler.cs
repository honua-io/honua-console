using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Establishes the circuit's authenticated operator as the ambient partition key for the lifetime of every
/// inbound circuit activity (honua-console#256).
///
/// THE REGRESSION THIS FIXES. honua-console#252 partitioned the multi-operator browser host's
/// profile/session/bearer state by <see cref="IConsoleOperatorContext.CurrentOperatorKey"/>, but the
/// operator key was resolved from <see cref="Microsoft.AspNetCore.Http.HttpContext"/> with an
/// <c>AsyncLocal</c> ambient set on the <c>/auth/login</c> HTTP thread. In Blazor <b>Server interactive</b>
/// rendering there is NO active <c>HttpContext</c> (the initial HTTP request is long gone; component code
/// and the singleton honua-server clients' binding handler run on the circuit), and the sign-in thread's
/// execution context does not flow to the circuit. So every interactive operator resolved to the shared
/// anonymous partition — re-introducing the exact cross-operator bearer/identity bleed #252 set out to
/// fix.
///
/// A <see cref="CircuitHandler"/> is the Blazor-Server-idiomatic seam: it is constructed per circuit with
/// the circuit's scoped services. We resolve the circuit-scoped
/// <see cref="AuthenticationStateProvider"/> and, by overriding
/// <see cref="CircuitHandler.CreateInboundActivityHandler"/>, wrap EVERY inbound circuit activity (render,
/// event, JS-interop callback) so the operator key derived from the circuit's authentication state is set
/// as the ambient for that activity's execution context. All synchronous and awaited work that activity
/// triggers — including outbound honua-server calls through the singleton binding handler and the
/// operator-scoped stores — therefore partitions to the authenticated circuit operator, not the shared
/// anonymous partition.
/// </summary>
public sealed class CircuitOperatorContextHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public CircuitOperatorContextHandler(AuthenticationStateProvider authenticationStateProvider)
    {
        _authenticationStateProvider = authenticationStateProvider
            ?? throw new ArgumentNullException(nameof(authenticationStateProvider));
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return async context =>
        {
            // The circuit's AuthenticationStateProvider is the authoritative operator identity for
            // interactive rendering (fed by CascadingAuthenticationState from the persisted auth state).
            // Resolve it fresh per activity so a revalidated / signed-out circuit re-keys correctly.
            var authenticationState = await _authenticationStateProvider
                .GetAuthenticationStateAsync()
                .ConfigureAwait(false);
            var operatorKey = ConsoleOperatorContext.ResolveKey(authenticationState.User);

            ConsoleOperatorContext.SetCircuitOperatorKey(operatorKey);
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                ConsoleOperatorContext.SetCircuitOperatorKey(null);
            }
        };
    }
}
