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
        if (management == apiBaseUri) return null;
        if (!AdmittedByShippedConnectPolicy(management))
        {
            throw new InvalidOperationException("managementApiBaseUrl must be HTTPS, or loopback HTTP on localhost, 127.0.0.1 or [::1]. The shipped Content-Security-Policy blocks every other HTTP origin.");
        }

        return management;
    }

    // A manager on another origin needs an operator bearer. Cross-origin apiBaseUrl already signs in with one.
    // Same-origin builtin sign-in can request one. Same-origin OIDC has only the session cookie.
    public void EnsureManagementSignIn(Uri applicationBaseUri, Uri apiBaseUri, Uri? managementBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        ArgumentNullException.ThrowIfNull(apiBaseUri);
        if (managementBaseUri is null || SameOrigin(managementBaseUri, applicationBaseUri) || !SameOrigin(apiBaseUri, applicationBaseUri))
        {
            return;
        }

        if (Auth.Mode == "builtin") return;
        throw new InvalidOperationException("A separate managementApiBaseUrl needs builtin password sign-in on this origin, which issues an operator bearer for the manager. Same-origin OIDC has no bearer to send.");
    }

    internal static bool SameOrigin(Uri left, Uri right) =>
        Uri.Compare(left, right, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool AdmittedByShippedConnectPolicy(Uri uri)
    {
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.Equals("::1", StringComparison.OrdinalIgnoreCase);
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
