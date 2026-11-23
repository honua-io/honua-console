using System.Net;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.Web.Auth;

// Configuration-owned mapping; it cannot apply to another active environment.
public sealed class ConsoleManagerEndpoint(IConfiguration? configuration) : IConsoleManagerEndpoint
{
    private readonly string? _serving = configuration?["Honua:Server:BaseUrl"] ?? configuration?["HONUA_SERVER_BASE_URL"];
    private readonly string? _manager = configuration?["Honua:Manager:BaseUrl"] ?? configuration?["HONUA_MANAGER_BASE_URL"];

    public Uri? Resolve(Uri servingBaseUri)
    {
        if (string.IsNullOrWhiteSpace(_manager)) return servingBaseUri;
        if (!TryBase(_serving, out var configuredServing) || !TryBase(_manager, out var manager)
            || configuredServing != DirectoryUri(servingBaseUri)) return null;
        return manager;
    }

    private static bool TryBase(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)) return false;
        uri = DirectoryUri(parsed);
        return true;
    }

    private static Uri DirectoryUri(Uri value) => new(value.AbsoluteUri.TrimEnd('/') + "/");
}

// This handler belongs only on the authenticated management client. An operator bearer is bound to the
// session store of the server that issued it, so a separate management server refuses the serving bearer;
// safe reads go through the management sign-in's cookie session when one exists.
public sealed class ConsoleManagerRoutingHandler(
    IConsoleEnvironmentProfileStore profiles,
    IConsoleManagerEndpoint endpoint,
    Func<string, Uri, HttpClient?>? managementSession = null)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (!ConsoleServerHttp.MatchesExpectedProfile(request, profile))
            return new HttpResponseMessage(HttpStatusCode.Conflict) { RequestMessage = request };

        var source = profile is null ? null : new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/");
        var destination = source is null ? null : endpoint.Resolve(source);
        if (source is null || destination is null || request.RequestUri is not { IsAbsoluteUri: true } target
            || !source.IsBaseOf(target))
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        request.RequestUri = new Uri(destination, source.MakeRelativeUri(target));
        if (IsSafe(request.Method) && destination != source && managementSession?.Invoke(profile!.Id, destination) is { } session)
        {
            using (session)
            using (var read = WithoutBearer(request))
            {
                return await session.SendAsync(read, cancellationToken).ConfigureAwait(false);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsSafe(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Head;

    // The outer client has already sent the original message, so the session receives a copy.
    private static HttpRequestMessage WithoutBearer(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers.Where(header => header.Key != "Authorization"))
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return copy;
    }
}
