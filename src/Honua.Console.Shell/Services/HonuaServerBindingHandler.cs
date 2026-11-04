using System.Net.Http;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Each request is rewritten onto the active profile base URI while preserving the path relative to the startup base, and a shared API key is removed when an operator bearer exists.
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

        // A client that already built an absolute URI from the active profile must not have that
        // profile's base path applied a second time. This happens when the startup base path is
        // "/" and the request is already https://host/tenant/api/...
        if (IsUnderBase(current, activeBase))
        {
            return;
        }

        if (startupBase is not null
            && SharesAuthority(current, startupBase)
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

    private static bool SharesAuthority(Uri request, Uri basis) =>
        string.Equals(request.Scheme, basis.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(request.Authority, basis.Authority, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderBase(Uri request, Uri basis)
    {
        if (!SharesAuthority(request, basis))
        {
            return false;
        }

        var basePath = NormalizeDirectoryPath(basis.AbsolutePath);
        if (basePath == "/")
        {
            return true;
        }

        var path = request.AbsolutePath;
        return path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, basePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
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
