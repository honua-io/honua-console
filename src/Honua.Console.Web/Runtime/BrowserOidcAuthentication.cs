using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Web.Runtime;

// The server owns callback correlation, code exchange, provider tokens and the session cookie.
public sealed class BrowserOidcAuthentication(
    ConsoleApiClient api,
    BrowserAuthenticationStateProvider authentication,
    NavigationManager navigation) : IConsoleOidcAuthentication
{
    public bool IsAvailable => api.UsesCookieSession && !api.SupportsPasswordSignIn;

    public async Task<IReadOnlyList<ConsoleIdentityProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        return await api.GetIdentityProvidersAsync(cancellationToken);
    }

    public async Task<Uri> BeginAsync(string providerKey, string returnTo, CancellationToken cancellationToken = default)
    {
        RequireAvailable();
        var mountedReturnTo = navigation.ToConsoleHref(returnTo);
        var providers = await GetProvidersAsync(cancellationToken);
        if (!providers.Any(provider => provider.Key == providerKey))
        {
            throw new InvalidOperationException("The identity provider is unavailable.");
        }
        await authentication.PrepareCookieRequestAsync(cancellationToken);
        return await api.CreateAuthorizeUrlAsync(providerKey, mountedReturnTo, cancellationToken);
    }

    private void RequireAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("Server-brokered identity provider sign-in requires the same origin.");
        }
    }
}
