using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Native.Core.Security;

namespace Honua.Console.Native.Core.Tests;

public sealed class NativeServerTrustTests
{
    [Fact]
    public void Callback_WhenTrustedFingerprintIsSupplied_RejectsMismatchedOsTrustedCertificate()
    {
        using var pinnedCertificate = CreateCertificate("CN=Honua Pinned Server");
        using var presentedCertificate = CreateCertificate("CN=Honua Presented Server");
        var trustedFingerprint = NativeServerTrust.ComputeSha256Fingerprint(pinnedCertificate);
        var callback = NativeServerTrust.CreateServerValidationCallback(trustedFingerprint);

        var accepted = callback(this, presentedCertificate, null, SslPolicyErrors.None);

        Assert.False(accepted);
    }

    [Fact]
    public void Callback_WhenTrustedFingerprintMatches_AcceptsCertificateDespitePolicyErrors()
    {
        using var certificate = CreateCertificate("CN=Honua Pinned Server");
        var trustedFingerprint = NativeServerTrust.ComputeSha256Fingerprint(certificate);
        var callback = NativeServerTrust.CreateServerValidationCallback(trustedFingerprint);

        var accepted = callback(this, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(accepted);
    }

    [Fact]
    public void Callback_WhenNoTrustedFingerprintIsSupplied_UsesOsTrustDecision()
    {
        using var certificate = CreateCertificate("CN=Honua Public Server");
        var callback = NativeServerTrust.CreateServerValidationCallback(trustedFingerprint: null);

        Assert.True(callback(this, certificate, null, SslPolicyErrors.None));
        Assert.False(callback(this, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    private static X509Certificate2 CreateCertificate(string subjectName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
    }
}
