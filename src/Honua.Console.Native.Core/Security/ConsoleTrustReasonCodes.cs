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
}
