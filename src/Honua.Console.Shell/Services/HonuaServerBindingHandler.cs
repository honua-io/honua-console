using System.Net.Http;
using System.Net.Http.Headers;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Security;

namespace Honua.Console.Shell.Services;

/// <summary>
/// One binding model for ALL honua-server clients (honua-console#234). The legacy "Family A"
/// catalog/content/operate/temporal/share/RBAC/Studio clients were bound once at DI time to the
/// startup <c>honuaServerBaseUrl</c> and authenticated with a process-wide admin <c>X-API-Key</c>.
/// That had three failures: switching the active environment profile silently mis-targeted mutations
/// to the startup server; every request ran as the shared admin principal (defeating per-principal
/// RBAC); and the native host (no startup URL) froze every Family-A surface into "Unsupported".
///
/// This <see cref="DelegatingHandler"/> makes those clients profile/session-aware at request time —
/// exactly like the Family-B observability client — without rewriting each typed client:
/// <list type="number">
/// <item>It rewrites the outgoing request's authority (scheme/host/port) to the active environment
/// profile's <c>ServerBaseUri</c>, so a mutation always targets the active profile's server. When
/// there is no active profile it leaves the DI-time base address untouched (preserving prior
/// behaviour for the configured browser host).</item>
/// <item>It attaches the active operator's bearer token (from the account session) as
/// <c>Authorization: Bearer</c> and removes any shared <c>X-API-Key</c> the inner client added, so
/// the request runs as the real operator principal. Only when no operator bearer exists does it
/// leave the admin-key fallback in place (documented in <c>docs/console-authentication.md</c>).</item>
/// </list>
/// The profile + session stores are singletons, so resolving them per request is safe and cheap.
/// </summary>
public sealed class HonuaServerBindingHandler : DelegatingHandler
{
    private readonly IConsoleEnvironmentProfileStore _profiles;
    private readonly IConsoleAccountSessionStore _sessions;

    public HonuaServerBindingHandler(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is not null)
        {
            RetargetToActiveProfile(request, profile.ServerBaseUri);

            var bearer = await ResolveOperatorBearerAsync(profile, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(bearer))
            {
                // Run as the real operator principal: forward the bearer and drop the shared admin
                // key so honua-server's per-principal RBAC (not the admin super-principal) decides.
                request.Headers.Remove("X-API-Key");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // Replaces only the authority of an already-absolute request URI (HttpClient resolves the inner
    // client's relative path against its BaseAddress before the handler runs) with the active
    // profile's authority, preserving the path + query so the same contract hits the active server.
    private static void RetargetToActiveProfile(HttpRequestMessage request, Uri activeBase)
    {
        var current = request.RequestUri;
        if (current is null || !current.IsAbsoluteUri)
        {
            return;
        }

        if (string.Equals(current.Scheme, activeBase.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Authority, activeBase.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var builder = new UriBuilder(current)
        {
            Scheme = activeBase.Scheme,
            Host = activeBase.Host,
            Port = activeBase.IsDefaultPort ? -1 : activeBase.Port
        };
        request.RequestUri = builder.Uri;
    }

    private async Task<string?> ResolveOperatorBearerAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.Account.AuthMode == ConsoleAccountAuthMode.Anonymous)
        {
            return null;
        }

        var session = await _sessions.GetSessionAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var token = session?.AccessToken;

        // A Console session sentinel marks "operator signed in" for read context but is not a real
        // honua-server bearer; do not forward it — let the admin-key fallback handle the call.
        return ConsoleAuthConstants.IsSessionSentinel(token) ? null : token;
    }
}
