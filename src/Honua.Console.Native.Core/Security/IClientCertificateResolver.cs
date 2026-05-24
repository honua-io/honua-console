using System.Security.Cryptography.X509Certificates;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

public interface IClientCertificateResolver
{
    ValueTask<X509Certificate2?> ResolveAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}
