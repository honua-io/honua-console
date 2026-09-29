using System.Net;
using System.Net.Http.Headers;
using Honua.Console.Shell.Services;

namespace Honua.Console.Web.Auth;

/// <summary>
/// Last credential boundary before transport for interactive server requests. Typed clients may
/// stamp a service key, but the browser host can only forward the active operator's bearer.
/// Server authorization remains responsible for scopes, tenant/owner checks and approval policy.
/// </summary>
public sealed class ConsoleOperatorCredentialHandler(
    IConsoleOperatorContext operators,
    IConsoleEnvironmentProfileStore profiles,
    IConsoleAccountSessionStore sessions,
    IConsoleOperatorBearerProvider bearers,
    bool allowAnonymous = false) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-API-Key");
        request.Headers.Authorization = null;

        if (!operators.HasOperator)
        {
            return allowAnonymous
                ? await base.SendAsync(request, cancellationToken)
                : Reauthenticate(request);
        }

        var profile = await profiles.GetActiveProfileAsync(cancellationToken);
        if (profile is null || request.RequestUri is not { IsAbsoluteUri: true } target
            || !new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/").IsBaseOf(target))
        {
            return Reauthenticate(request);
        }

        var credential = await bearers.ResolveAsync(profile, cancellationToken);
        var session = await sessions.GetSessionAsync(profile.Id, cancellationToken);
        if (!credential.IsAvailable || session?.ServerBaseUri != profile.ServerBaseUri)
        {
            return Reauthenticate(request);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static HttpResponseMessage Reauthenticate(HttpRequestMessage request)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            RequestMessage = request,
            Content = new StringContent(
                "{\"message\":\"Sign in to honua-server again before retrying this request.\"}",
                System.Text.Encoding.UTF8, "application/json")
        };
        response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\""));
        response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        return response;
    }
}
