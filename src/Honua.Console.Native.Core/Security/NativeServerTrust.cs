using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Server-certificate fingerprinting and trust-on-first-use validation used by the native HTTP and
/// gRPC transports. The server exposes no fingerprint endpoint, so the observed fingerprint is pinned
/// client-side in the profile state and compared on each connection.
/// </summary>
public static class NativeServerTrust
{
    /// <summary>SHA-256 fingerprint of a presented server certificate as uppercase hex.</summary>
    public static string ComputeSha256Fingerprint(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var hash = SHA256.HashData(certificate.GetRawCertData());
        return Convert.ToHexString(hash);
    }

    /// <summary>SHA-256 thumbprint of a client certificate as uppercase hex.</summary>
    public static string ComputeSha256Thumbprint(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Builds a server-certificate validation callback that captures the observed fingerprint and
    /// accepts the connection when the OS chain is valid or the fingerprint matches a pinned/trusted
    /// value (trust-on-first-use pinning). Used for the validate call so a self-signed or
    /// privately-issued server identity that the operator already pinned is accepted.
    /// </summary>
    public static RemoteCertificateValidationCallback CreateServerValidationCallback(
        string? trustedFingerprint,
        Action<string>? onObserved = null)
    {
        return (_, certificate, _, errors) =>
        {
            if (certificate is null)
            {
                return false;
            }

            var fingerprint = ComputeSha256Fingerprint(certificate);
            onObserved?.Invoke(fingerprint);

            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            return !string.IsNullOrEmpty(trustedFingerprint)
                && string.Equals(fingerprint, trustedFingerprint, StringComparison.OrdinalIgnoreCase);
        };
    }
}
