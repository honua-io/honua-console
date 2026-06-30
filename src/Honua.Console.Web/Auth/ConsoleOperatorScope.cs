using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Circuit/request-scoped accessor for the operator the current execution context is acting as
/// (honua-console#254). This is the fail-closed-by-construction replacement for the singleton
/// <see cref="IConsoleOperatorContext"/> + process-static ambient operator key on the server-bound call
/// path. It is registered <c>AddScoped</c>, so each inbound HTTP request and each interactive circuit
/// gets its OWN instance that reads its OWN authoritative identity source — there is no shared mutable
/// singleton and no <c>AsyncLocal</c> ambient that a missed execution context could silently leave
/// unset (which is how an authenticated operator's call previously collapsed into the shared anonymous
/// partition / admin-key fallback).
///
/// Resolution is from a single authoritative source per scope, with no cross-source fallback that could
/// fail open:
/// <list type="number">
/// <item>when the scope has an <see cref="HttpContext"/> (the request pipeline — e.g. the admin-keyed
/// map-proxy endpoints, the sign-in request) the operator is taken from
/// <see cref="HttpContext.User"/>, the request's authenticated principal;</item>
/// <item>otherwise the scope is an interactive circuit (no <see cref="HttpContext"/>), and the operator
/// is taken from the circuit's scoped <see cref="AuthenticationStateProvider"/> — the same authoritative
/// source <see cref="CircuitOperatorContextHandler"/> uses, but read directly from this scope rather than
/// stamped onto an ambient.</item>
/// </list>
/// A scope whose authoritative source reports no authenticated operator yields <see langword="null"/>
/// from <see cref="ResolveAsync"/> and throws from <see cref="RequireAsync"/>; it NEVER yields a shared
/// "anonymous" operator. Server-bound call sites that must act with honua-server privileges call
/// <see cref="RequireAsync"/> (or treat a <c>null</c> resolve as a hard deny), so an unresolved operator
/// is structurally unable to proceed.
/// </summary>
public interface IConsoleOperatorScope
{
    /// <summary>
    /// Resolves the operator for the current scope, or <see langword="null"/> when the scope's
    /// authoritative identity source reports no authenticated operator (a genuinely anonymous request or
    /// circuit). Never returns a shared/ambient "anonymous" operator.
    /// </summary>
    ValueTask<ConsoleOperatorIdentity?> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the operator for the current scope, or fails closed. Use on any path that acts with
    /// honua-server privileges so an unresolved operator can never silently proceed.
    /// </summary>
    /// <exception cref="ConsoleOperatorContextUnresolvedException">
    /// No authenticated operator is in scope.
    /// </exception>
    ValueTask<ConsoleOperatorIdentity> RequireAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IConsoleOperatorScope"/>
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
