using System.Text.Json.Serialization;

namespace Honua.Console.Web.Runtime;

// Public deployment settings only. Credentials never belong in the static bundle.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsoleRuntimeConfiguration
{
    public string ApiBaseUrl { get; init; } = "/";
    public ConsoleBrowserAuthConfiguration Auth { get; init; } = new();
    public bool V4Enabled { get; init; } = true;

    public Uri ResolveApiBaseUri(Uri applicationBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        if (string.IsNullOrWhiteSpace(ApiBaseUrl) || ApiBaseUrl.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(applicationBaseUri, ApiBaseUrl, out var api)
            || (api.Scheme != Uri.UriSchemeHttps && !(api.Scheme == Uri.UriSchemeHttp && api.IsLoopback))
            || api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0)
        {
            throw new InvalidOperationException("apiBaseUrl must be an HTTPS URL or a local development HTTP URL, without credentials, query or fragment.");
        }

        if (Auth is null)
        {
            throw new InvalidOperationException("auth must be configured.");
        }
        Auth.Validate();
        return new Uri(api.AbsoluteUri.TrimEnd('/') + "/");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsoleBrowserAuthConfiguration
{
    public string Mode { get; init; } = "builtin";
    public string? Authority { get; init; }
    public string? ClientId { get; init; }
    public string[] Scopes { get; init; } = [];

    internal void Validate()
    {
        if (Mode is not ("builtin" or "oidc"))
        {
            throw new InvalidOperationException("auth.mode must be builtin or oidc.");
        }

        if (Mode == "oidc" && (!Uri.TryCreate(Authority, UriKind.Absolute, out var authority)
            || authority.Scheme != Uri.UriSchemeHttps || authority.UserInfo.Length != 0
            || authority.Query.Length != 0 || authority.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(ClientId) || Scopes is null || Scopes.Length == 0
            || Scopes.Any(string.IsNullOrWhiteSpace)))
        {
            throw new InvalidOperationException("OIDC requires an HTTPS authority, clientId and scopes.");
        }
    }
}
