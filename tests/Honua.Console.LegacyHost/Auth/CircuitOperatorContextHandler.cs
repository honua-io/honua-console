using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Honua.Console.Web.Auth;

// Interactive circuits have no HttpContext, so each inbound activity sets the operator key from the circuit authentication state.
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
