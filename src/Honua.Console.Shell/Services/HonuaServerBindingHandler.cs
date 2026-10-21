using System.Net.Http;
using Honua.Console.Shell.Models;

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
/// <item>It rewrites the outgoing request onto the active environment profile's
/// <c>ServerBaseUri</c>, including that profile's base path. The path relative to the startup
/// base is preserved, so a gateway prefix on the active profile is not dropped and the startup
/// prefix is not sent to a different environment. When there is no active profile it leaves the
/// DI-time base address untouched.</item>
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
    private readonly Uri? _startupBaseUri;

    public HonuaServerBindingHandler(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions,
        Uri? startupBaseUri = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _startupBaseUri = startupBaseUri;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is not null)
        {
            RetargetToActiveProfile(request, profile.ServerBaseUri, _startupBaseUri);

            // The key, when present, was attached by the typed client. Passing no new key keeps it
            // as the headless fallback while the shared helper prefers the human operator bearer.
            await ConsoleServerHttp.AttachAuthenticationAsync(
                request,
                _sessions,
                profile,
                adminApiKey: null,
                cancellationToken).ConfigureAwait(false);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // HttpClient has already resolved the relative path against the startup BaseAddress. Rebuild that
    // path against the active profile base so a profile mounted at /tenant keeps /tenant and does not
    // inherit the startup prefix. Without a startup base, only the authority changes.
    private static void RetargetToActiveProfile(HttpRequestMessage request, Uri activeBase, Uri? startupBase)
    {
        var current = request.RequestUri;
        if (current is null || !current.IsAbsoluteUri)
        {
            return;
        }

        if (startupBase is not null
            && TryTakePathRelativeToBase(current, startupBase, out var relativePath, out var query))
        {
            var builder = new UriBuilder(activeBase)
            {
                Path = NormalizeDirectoryPath(activeBase.AbsolutePath) + relativePath,
                Query = query,
            };
            request.RequestUri = builder.Uri;
            return;
        }

        if (string.Equals(current.Scheme, activeBase.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Authority, activeBase.Authority, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var authorityOnly = new UriBuilder(current)
        {
            Scheme = activeBase.Scheme,
            Host = activeBase.Host,
            Port = activeBase.IsDefaultPort ? -1 : activeBase.Port
        };
        request.RequestUri = authorityOnly.Uri;
    }

    private static string NormalizeDirectoryPath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath) || absolutePath == "/")
        {
            return "/";
        }

        return absolutePath.EndsWith('/') ? absolutePath : absolutePath + "/";
    }

    private static bool TryTakePathRelativeToBase(
        Uri requestUri,
        Uri startupBase,
        out string relativePath,
        out string query)
    {
        query = requestUri.Query.TrimStart('?');
        var basePath = startupBase.AbsolutePath;
        var requestPath = requestUri.AbsolutePath;
        if (string.IsNullOrEmpty(basePath) || basePath == "/")
        {
            relativePath = requestPath.TrimStart('/');
            return true;
        }

        var prefix = basePath.EndsWith('/') ? basePath : basePath + "/";
        if (requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = requestPath[prefix.Length..];
            return true;
        }

        if (string.Equals(requestPath, prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            relativePath = string.Empty;
            return true;
        }

        relativePath = string.Empty;
        return false;
    }

}
