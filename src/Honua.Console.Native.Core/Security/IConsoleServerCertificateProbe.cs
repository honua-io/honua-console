using System.Security.Cryptography.X509Certificates;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Observes the server's presented TLS certificate fingerprint so the trust gate can pin it and
/// detect server-identity changes. Returns <c>null</c> when the server is unreachable or the
/// fingerprint cannot be observed.
/// </summary>
public interface IConsoleServerCertificateProbe
{
    Task<string?> ObserveServerFingerprintAsync(
        ConsoleEnvironmentProfile profile,
        X509Certificate2? clientCertificate,
        CancellationToken cancellationToken = default);
}
