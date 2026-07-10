using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record ConsoleEnvironmentProfile
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public Uri ServerBaseUri { get; init; } = new("https://localhost");

    public string EnvironmentKind { get; init; } = "development";

    public string TenantId { get; init; } = string.Empty;

    public ConsoleEnvironmentTransportCapabilities TransportCapabilities { get; init; } = new();

    public ConsoleAccountBinding Account { get; init; } = new();

    public ConsoleClientCertificateBinding ClientCertificate { get; init; } = new();

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ConsoleEnvironmentTransportCapabilities
{
    public bool BrowserHttp { get; init; } = true;

    public bool BrowserRealtime { get; init; } = true;

    public bool NativeGrpc { get; init; }

    public bool NativeMtls { get; init; }
}

public sealed record ConsoleAccountBinding
{
    public ConsoleAccountAuthMode AuthMode { get; init; } = ConsoleAccountAuthMode.AccountRbac;

    public string AccountId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string[] PermissionHints { get; init; } = [];
}

public enum ConsoleAccountAuthMode
{
    Anonymous,
    AccountRbac,

    /// <summary>
    /// Explicit non-interactive profile whose mutations may use a configured service
    /// API key only when the host also selects <c>HeadlessService</c> credential mode.
    /// Interactive profile creation does not offer this mode.
    /// </summary>
    ServiceApiKey
}

public sealed record ConsoleClientCertificateBinding
{
    public bool Enabled { get; init; }

    public ConsoleClientCertificateReference? Reference { get; init; }

    /// <summary>Optional server trust profile id to pass to honua-server certificate validation.</summary>
    public string TrustProfileId { get; init; } = string.Empty;
}

public sealed record ConsoleClientCertificateReference
{
    public ConsoleClientCertificateReferenceKind Kind { get; init; } = ConsoleClientCertificateReferenceKind.None;

    public string Value { get; init; } = string.Empty;

    public string SecretName { get; init; } = string.Empty;

    public string StoreName { get; init; } = "My";

    public string StoreLocation { get; init; } = "CurrentUser";
}

public enum ConsoleClientCertificateReferenceKind
{
    None,
    FilePath,
    StoreThumbprint,
    StoreSubject
}

public sealed record ConsoleEnvironmentState
{
    public string ProfileId { get; init; } = string.Empty;

    public string LastRoute { get; init; } = "/";

    public string LastStreamingResumeToken { get; init; } = string.Empty;

    public DateTimeOffset? LastConnectedAt { get; init; }

    // Console-owned trust pins (local/session state, exempt from the no-mock rule per
    // Console Patterns Charter section 11). The server exposes no fingerprint or
    // capability-discovery endpoint, so server-identity pinning is client-side.

    /// <summary>Acknowledged server certificate SHA-256 fingerprint used to detect server-identity changes.</summary>
    public string PinnedServerFingerprint { get; init; } = string.Empty;

    /// <summary>Acknowledged bound client certificate SHA-256 thumbprint used to detect client-certificate changes.</summary>
    public string PinnedClientCertificateThumbprint { get; init; } = string.Empty;

    /// <summary>True when the native connection is refused until the operator acknowledges or revalidates.</summary>
    public bool TrustBlocked { get; init; }

    /// <summary>Last server-validated trust state (shim of honua-sdk-dotnet#166 HonuaEnvironmentTrustState).</summary>
    public HonuaEnvironmentTrustState? Trust { get; init; }

    public Dictionary<string, string> Diagnostics { get; init; } = new(StringComparer.Ordinal);
}

public sealed record ConsoleAccountSession
{
    public string ProfileId { get; init; } = string.Empty;

    public string AccountId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string[] RoleIds { get; init; } = [];

    public string[] PermissionIds { get; init; } = [];

    public string AccessToken { get; init; } = string.Empty;

    /// <summary>
    /// Gets the expiry of the forwardable honua-server operator bearer, when known.
    /// A null value is retained for edge-forwarded access tokens whose expiry is
    /// managed by the trusted edge rather than exposed to the Console.
    /// </summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
}
