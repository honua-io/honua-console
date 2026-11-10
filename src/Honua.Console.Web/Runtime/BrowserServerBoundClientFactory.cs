using System.Net.Http.Headers;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Honua.Console.Web.Runtime;

// Existing typed Shell/SDK clients share this transport. No browser path can fall back to sockets.
public sealed class BrowserServerBoundClientFactory(
    Uri apiBaseUri,
    Uri applicationBaseUri,
    ConsoleBrowserSession session) : IHonuaServerBoundClientFactory
{
    public HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri, timeout, BrowserApiAccess.Authenticated);
    public HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri, timeout, BrowserApiAccess.Public);

    private HttpClient Create(Uri baseUri, TimeSpan? timeout, BrowserApiAccess access) => new(
        new BrowserApiHandler(new BrowserApiOrigins(apiBaseUri, applicationBaseUri), session, access)
        {
            InnerHandler = new HttpClientHandler()
        })
    {
        BaseAddress = baseUri.Host == "unbound.invalid" ? apiBaseUri : baseUri,
        Timeout = timeout ?? TimeSpan.FromSeconds(30)
    };
}

internal sealed class BrowserApiHandler(
    BrowserApiOrigins origins,
    ConsoleBrowserSession session,
    BrowserApiAccess access) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri;
        if (target is null || !target.IsAbsoluteUri || !origins.Api.IsBaseOf(target)
            || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
        {
            throw new HttpRequestException("Console requests must target the configured Honua API.");
        }

        if (access == BrowserApiAccess.Authenticated && !session.IsAuthenticated)
        {
            throw new HttpRequestException("Sign in to Honua before accessing this API.", null, System.Net.HttpStatusCode.Unauthorized);
        }

        // Never forward keys or caller-supplied credentials from a typed client.
        request.Headers.Remove("X-API-Key");
        request.Headers.Remove("Cookie");
        request.Headers.Remove("X-CSRF-Token");
        request.Headers.Authorization = null;
        var sameOrigin = Uri.Compare(origins.Api, origins.Application, UriComponents.SchemeAndServer,
            UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
        if (session.IsAuthenticated && !string.IsNullOrWhiteSpace(session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }
        else if (!sameOrigin && access == BrowserApiAccess.Authenticated)
        {
            throw new HttpRequestException("Cross-origin Console access requires an operator bearer token.", null, System.Net.HttpStatusCode.Unauthorized);
        }

        if (sameOrigin && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
            && request.Method != HttpMethod.Options)
        {
            if (string.IsNullOrWhiteSpace(session.CsrfToken))
            {
                throw new HttpRequestException("The API has not issued a CSRF token. Sign in again before making changes.");
            }
            request.Headers.Add("X-CSRF-Token", session.CsrfToken);
        }

        request.SetBrowserRequestCredentials(sameOrigin ? BrowserRequestCredentials.SameOrigin : BrowserRequestCredentials.Omit);
        request.SetBrowserRequestOption("redirect", "error");
        return base.SendAsync(request, cancellationToken);
    }
}

internal sealed record BrowserApiOrigins(Uri Api, Uri Application);

internal enum BrowserApiAccess
{
    Authenticated,
    Public
}
