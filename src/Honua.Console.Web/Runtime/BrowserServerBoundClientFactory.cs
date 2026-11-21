using System.Net.Http.Headers;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Honua.Console.Web.Runtime;

// Existing typed Shell/SDK clients share this transport. No browser path can fall back to sockets.
public sealed class BrowserServerBoundClientFactory(
    BrowserApiOrigins origins,
    ConsoleBrowserSession session) : IHonuaServerBoundClientFactory
{
    public HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri, timeout, BrowserApiAccess.Authenticated);
    public HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri, timeout, BrowserApiAccess.Public);
    public HttpClient CreateManagerBoundClient(Uri baseUri, TimeSpan? timeout = null) => Create(baseUri, timeout, BrowserApiAccess.Manager);

    private HttpClient Create(Uri baseUri, TimeSpan? timeout, BrowserApiAccess access) => new(
        new BrowserApiHandler(origins, session, access)
        {
            InnerHandler = new HttpClientHandler()
        })
    {
        BaseAddress = baseUri.Host == "unbound.invalid" ? origins.Api : baseUri,
        Timeout = timeout ?? TimeSpan.FromSeconds(30)
    };
}

internal sealed class BrowserApiHandler(
    BrowserApiOrigins origins,
    ConsoleBrowserSession session,
    BrowserApiAccess access) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = session.CaptureAuthority(origins.Api);
        var pinned = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var expected);
        var capture = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Capture, out var requested) && requested;
        if ((pinned && (current is null || !string.Equals(expected, current, StringComparison.Ordinal)))
            || (capture && current is null))
            throw new HttpRequestException("The Console session changed. Refresh before making another request.");

        var binding = pinned || capture ? current : null;
        var sessionRevision = session.Revision;
        PrepareRequest(request);
        if (request.Headers.Accept.Any(value => value.MediaType == "text/event-stream"))
            request.SetBrowserResponseStreamingEnabled(true);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && access == BrowserApiAccess.Authenticated && session.Revision == sessionRevision)
        {
            session.Clear();
            return response;
        }
        if (binding is not null && !string.Equals(binding, session.CaptureAuthority(origins.Api), StringComparison.Ordinal))
        {
            response.Dispose();
            request.Options.Set(ConsoleWriteOutcome.Unverified, true);
            return new HttpResponseMessage(System.Net.HttpStatusCode.Conflict);
        }
        if (binding is not null) request.Options.Set(ConsoleAuthorityRequestOptions.Resolved, binding);
        return response;
    }

    private void PrepareRequest(HttpRequestMessage request)
    {
        var target = ServingTarget(request.RequestUri);
        if (access == BrowserApiAccess.Manager)
        {
            target = ManagementTarget(target);
            request.RequestUri = target;
        }

        if (access != BrowserApiAccess.Public && !session.IsAuthenticated)
        {
            throw new HttpRequestException("Sign in to Honua before accessing this API.", null, System.Net.HttpStatusCode.Unauthorized);
        }

        ApplyOperatorCredentials(request, target);
    }

    private Uri ServingTarget(Uri? target)
    {
        if (target is null || !target.IsAbsoluteUri || !origins.Api.IsBaseOf(target)
            || target.UserInfo.Length != 0 || target.Fragment.Length != 0)
        {
            throw new HttpRequestException("Console requests must target the configured Honua API.");
        }

        return target;
    }

    private Uri ManagementTarget(Uri target)
    {
        if (origins.Management is null) return target;
        var rewritten = new Uri(origins.Management, origins.Api.MakeRelativeUri(target));
        if (!origins.Management.IsBaseOf(rewritten) || rewritten.UserInfo.Length != 0 || rewritten.Fragment.Length != 0)
        {
            throw new HttpRequestException("Console requests must target the configured management API.");
        }

        return rewritten;
    }

    private void ApplyOperatorCredentials(HttpRequestMessage request, Uri target)
    {
        // Never forward keys or caller-supplied credentials from a typed client.
        request.Headers.Remove("X-API-Key");
        request.Headers.Remove("Cookie");
        request.Headers.Remove("X-CSRF-Token");
        request.Headers.Remove("X-Honua-CSRF");
        request.Headers.Authorization = null;
        var sameOrigin = ConsoleRuntimeConfiguration.SameOrigin(target, origins.Application);
        var managerOnServingOrigin = origins.Management is { } management
            && ConsoleRuntimeConfiguration.SameOrigin(management, origins.Application)
            && ConsoleRuntimeConfiguration.SameOrigin(origins.Api, origins.Application);
        var managementCall = access == BrowserApiAccess.Manager && origins.Management is not null && !managerOnServingOrigin;
        var bearer = managementCall ? session.ManagementAccessToken ?? session.AccessToken : session.AccessToken;
        var bearerExpiry = managementCall && session.ManagementAccessToken is not null
            ? session.ManagementExpiresAt
            : session.ExpiresAt;
        var bearerCall = managementCall || !sameOrigin;
        var omitCredentials = request.Options.TryGetValue(BrowserCredentialRequestOptions.Omit, out var omit) && omit;
        if (bearerCall && !omitCredentials
            && !string.IsNullOrWhiteSpace(bearer)
            && bearerExpiry > DateTimeOffset.UtcNow
            && (session.IsAuthenticated || access == BrowserApiAccess.Public))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        else if (bearerCall && access != BrowserApiAccess.Public)
        {
            throw new HttpRequestException(
                managementCall
                    ? "Sign in with the deployment password before using the management endpoint."
                    : "Cross-origin Console access requires an operator bearer token.",
                null, System.Net.HttpStatusCode.Unauthorized);
        }

        if (!bearerCall && !omitCredentials && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head
            && request.Method != HttpMethod.Options)
        {
            if (string.IsNullOrWhiteSpace(session.CsrfToken))
            {
                throw new HttpRequestException("The API has not issued a CSRF token. Sign in again before making changes.");
            }
            request.Headers.Add("X-Honua-CSRF", session.CsrfToken);
        }

        request.SetBrowserRequestCredentials(bearerCall || omitCredentials ? BrowserRequestCredentials.Omit : BrowserRequestCredentials.SameOrigin);
        request.SetBrowserRequestOption("redirect", "error");
    }
}

// The server refuses a password-token request that carries the browser session (cookie or CSRF token).
internal static class BrowserCredentialRequestOptions
{
    internal static readonly HttpRequestOptionsKey<bool> Omit = new("Honua.Console.OmitBrowserCredentials");
}

public sealed record BrowserApiOrigins(Uri Api, Uri Application, Uri? Management = null);

internal enum BrowserApiAccess
{
    Authenticated,
    Public,
    Manager
}
