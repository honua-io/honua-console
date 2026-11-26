using System.Net;
using System.Text.RegularExpressions;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Web.Runtime;

// The server owns callback correlation, code exchange, provider tokens and the session cookie.
public sealed partial class BrowserOidcAuthentication(
    ConsoleApiClient api,
    BrowserAuthenticationStateProvider authentication,
    NavigationManager navigation) : IConsoleOidcAuthentication
{
    internal const string HomeReturnPath = "/console/";
    private const string AuthRoute = "/console/auth";
    private const string AuthRoutePrefix = "/console/auth/";
    private const int MaxReturnPathLength = 256;

    // Built-in password sign-in does not exclude providers: the server's /auth/config decides which it lists.
    public bool IsAvailable => api.UsesCookieSession;

    internal static bool IsAllowedReturnPath(string? path) =>
        path is not null
        && path.Length <= MaxReturnPathLength
        && !path.Equals(AuthRoute, StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith(AuthRoutePrefix, StringComparison.OrdinalIgnoreCase)
        && AllowedReturnPath().IsMatch(path);

    public async Task<IReadOnlyList<ConsoleIdentityProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return await api.GetIdentityProvidersAsync(cancellationToken);
    }

    public async Task<Uri> BeginAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        var mountedReturnTo = ResolveReturnPath(returnTo);
        var providers = await GetProvidersAsync(cancellationToken);
        if (!providers.Any(provider => provider.Key == providerKey))
        {
            throw new InvalidOperationException("The identity provider is unavailable.");
        }

        await authentication.PrepareCookieRequestAsync(cancellationToken);
        return await RequestAuthorizeUrlAsync(providerKey, mountedReturnTo, cancellationToken);
    }

    private string ResolveReturnPath(string returnTo)
    {
        var mountedReturnTo = navigation.ToConsoleHref(returnTo);
        return IsAllowedReturnPath(mountedReturnTo) ? mountedReturnTo : HomeReturnPath;
    }

    private async Task<Uri> RequestAuthorizeUrlAsync(
        string providerKey,
        string mountedReturnTo,
        CancellationToken cancellationToken)
    {
        try
        {
            return await api.CreateAuthorizeUrlAsync(providerKey, mountedReturnTo, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.BadRequest
            && mountedReturnTo != HomeReturnPath)
        {
            // Older servers reject deep return paths before starting the sign-in attempt.
            return await api.CreateAuthorizeUrlAsync(providerKey, HomeReturnPath, cancellationToken);
        }
    }

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Server-brokered identity provider sign-in requires the same origin.");
        }
    }

    [GeneratedRegex(@"^/console/(?:[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+)*)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedReturnPath();
}
