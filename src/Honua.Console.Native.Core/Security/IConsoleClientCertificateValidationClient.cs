using System.Security.Cryptography.X509Certificates;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Validates a bound client certificate against a real honua-server's trust profiles
/// (<c>POST /api/v1/admin/security/client-certificates/validate</c>, honua-server#1171). This is the
/// server-owned trust binding; there is no standing mock for it (Console Patterns Charter section 11).
/// </summary>
public interface IConsoleClientCertificateValidationClient
{
    /// <param name="trustedServerFingerprint">
    /// Pinned/observed server fingerprint to accept when the OS chain is not trusted (e.g. a private
    /// or self-signed server identity the operator already pinned). Pass <c>null</c> to require OS trust.
    /// </param>
    Task<ConsoleClientCertificateValidationResult> ValidateAsync(
        ConsoleEnvironmentProfile profile,
        X509Certificate2 clientCertificate,
        string? trustedServerFingerprint = null,
        CancellationToken cancellationToken = default);
}
