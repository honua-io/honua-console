using System.Text.Json.Serialization;

namespace Honua.Console.Web.Runtime;

// Public deployment settings only. Credentials never belong in the static bundle.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConsoleRuntimeConfiguration
{
    public string ApiBaseUrl { get; init; } = "/";

    // Null means management calls stay on the serving API. An empty value is not a configured endpoint.
    public string? ManagementApiBaseUrl { get; init; }

    public ConsoleBrowserAuthConfiguration Auth { get; init; } = new();
    public bool V4Enabled { get; init; } = true;

    public Uri ResolveApiBaseUri(Uri applicationBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        if (Auth is null)
        {
            throw new InvalidOperationException("auth must be configured.");
        }
        Auth.Validate();
        return RequireBase(applicationBaseUri, ApiBaseUrl, "apiBaseUrl");
    }

    public Uri? ResolveManagementBaseUri(Uri applicationBaseUri, Uri apiBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        if (ManagementApiBaseUrl is null) return null;
        var management = RequireBase(applicationBaseUri, ManagementApiBaseUrl, "managementApiBaseUrl");
        return management == apiBaseUri ? null : management;
    }

    private static Uri RequireBase(Uri applicationBaseUri, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(applicationBaseUri, value, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && !(parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
            || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0)
        {
            throw new InvalidOperationException(name + " must be an HTTPS URL or a local development HTTP URL, without credentials, query or fragment.");
        }

        return new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/");
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
