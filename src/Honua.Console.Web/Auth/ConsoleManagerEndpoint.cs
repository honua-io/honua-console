using System.Net;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.Web.Auth;

/// <summary>Configuration-owned mapping; it cannot apply to another active environment.</summary>
public sealed class ConsoleManagerEndpoint(IConfiguration? configuration) : IConsoleManagerEndpoint
{
    private readonly string? _serving = configuration?["Honua:Server:BaseUrl"] ?? configuration?["HONUA_SERVER_BASE_URL"];
    private readonly string? _manager = configuration?["Honua:Manager:BaseUrl"] ?? configuration?["HONUA_MANAGER_BASE_URL"];

    /// <inheritdoc />
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

/// <summary>Retargets only the authenticated management client to the configured manager.</summary>
public sealed class ConsoleManagerRoutingHandler(IConsoleEnvironmentProfileStore profiles, IConsoleManagerEndpoint endpoint)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var source = profile is null ? null : new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/");
        var destination = source is null ? null : endpoint.Resolve(source);
        if (source is null || destination is null || request.RequestUri is not { IsAbsoluteUri: true } target
            || !source.IsBaseOf(target))
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        request.RequestUri = new Uri(destination, source.MakeRelativeUri(target));
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
