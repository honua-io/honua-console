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

    // Mutations may use a service API key only when the host also selects HeadlessService mode.
    ServiceApiKey
}

public sealed record ConsoleClientCertificateBinding
{
    public bool Enabled { get; init; }

    public ConsoleClientCertificateReference? Reference { get; init; }

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

    public DateTimeOffset? LastConnectedAt { get; init; }

    // Console-owned trust pins (local/session state, exempt from the no-mock rule per
    // Console Patterns Charter section 11). The server exposes no fingerprint or
    // capability-discovery endpoint, so server-identity pinning is client-side.

    public string PinnedServerFingerprint { get; init; } = string.Empty;

    public string PinnedClientCertificateThumbprint { get; init; } = string.Empty;

    public bool TrustBlocked { get; init; }

    public HonuaEnvironmentTrustState? Trust { get; init; }
}

public sealed record ConsoleAccountSession
{
    public string ProfileId { get; init; } = string.Empty;

    public string AccountId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string[] RoleIds { get; init; } = [];

    public string[] PermissionIds { get; init; } = [];

    public Uri? ServerBaseUri { get; init; }

    public string AccessToken { get; init; } = string.Empty;

    // Null means an edge-forwarded token whose expiry stays at the edge.
    public DateTimeOffset? AccessTokenExpiresAt { get; init; }
}
