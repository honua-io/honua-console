namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Host-side trust reason codes for client-pinned conditions the server does not report (it exposes
/// no server-certificate fingerprint or capability-discovery endpoint). Server-reported validation
/// codes use the stable <c>client_certificate_*</c> codes from honua-server#1171.
/// </summary>
public static class ConsoleTrustReasonCodes
{
    /// <summary>The observed server TLS fingerprint differs from the pinned (acknowledged) value.</summary>
    public const string ServerCertificateChanged = "server_certificate_changed";

    /// <summary>The bound client certificate identity differs from the pinned (acknowledged) value.</summary>
    public const string ClientCertificateChanged = "client_certificate_changed";

    /// <summary>
    /// An mTLS profile resolved a client certificate that has no usable private key (public-only), so
    /// it cannot complete client authentication. The connection is blocked as a <c>Missing</c> trust state.
    /// </summary>
    public const string ClientCertificatePrivateKeyUnavailable = "client_certificate_private_key_unavailable";
}
