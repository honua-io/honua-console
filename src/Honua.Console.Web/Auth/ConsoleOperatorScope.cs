using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

// An unresolved operator is null, not the shared anonymous partition key.
public interface IConsoleOperatorScope
{
    ValueTask<ConsoleOperatorIdentity?> ResolveAsync(CancellationToken cancellationToken = default);

    ValueTask<ConsoleOperatorIdentity> RequireAsync(CancellationToken cancellationToken = default);
}

public sealed class ConsoleOperatorScope : IConsoleOperatorScope
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public ConsoleOperatorScope(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authenticationStateProvider)
    {
        _httpContextAccessor = httpContextAccessor
            ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _authenticationStateProvider = authenticationStateProvider
            ?? throw new ArgumentNullException(nameof(authenticationStateProvider));
    }

    public async ValueTask<ConsoleOperatorIdentity?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        // The request pipeline carries the authenticated operator on HttpContext.User. When there is no
        // HttpContext at all the scope is an interactive circuit, whose authoritative identity is the
        // circuit's AuthenticationStateProvider. We deliberately do NOT cross-fall-back between the two
        // sources: a request whose HttpContext.User is unauthenticated is anonymous (it must not be
        // re-resolved from some other ambient), and a circuit with no authenticated state is anonymous.
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            return ConsoleOperatorIdentity.FromPrincipal(httpContext.User);
        }

        var state = await _authenticationStateProvider
            .GetAuthenticationStateAsync()
            .ConfigureAwait(false);
        return ConsoleOperatorIdentity.FromPrincipal(state.User);
    }

    public async ValueTask<ConsoleOperatorIdentity> RequireAsync(CancellationToken cancellationToken = default)
        => await ResolveAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ConsoleOperatorContextUnresolvedException();
}
