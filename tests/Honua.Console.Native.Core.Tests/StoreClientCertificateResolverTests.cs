using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class StoreClientCertificateResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenFileReferenceIsMissing_ReturnsNull()
    {
        var resolver = new StoreClientCertificateResolver(new InMemoryNativeSecretStore());
        var profile = new ConsoleEnvironmentProfile
        {
            Id = "prod-west",
            DisplayName = "Prod West",
            ClientCertificate = new ConsoleClientCertificateBinding
            {
                Enabled = true,
                Reference = new ConsoleClientCertificateReference
                {
                    Kind = ConsoleClientCertificateReferenceKind.FilePath,
                    Value = Path.Combine(Path.GetTempPath(), $"honua-console-missing-client-cert-{Guid.NewGuid():N}.pfx")
                }
            }
        };

        var certificate = await resolver.ResolveAsync(profile);

        Assert.Null(certificate);
    }
}
