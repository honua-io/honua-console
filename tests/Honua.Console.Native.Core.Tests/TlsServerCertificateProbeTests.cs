using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class TlsServerCertificateProbeTests
{
    [Fact]
    public async Task ObserveServerFingerprint_WhenCallerAlreadyCancelled_PropagatesCancellation()
    {
        var probe = new TlsServerCertificateProbe();
        // A literal IP avoids DNS, so caller cancellation is the only thing that can complete the
        // call. The probe must honor it instead of swallowing it into a null (unreachable) result.
        var profile = ProfileFor(new Uri("https://198.51.100.1:9/"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ObserveServerFingerprintAsync(profile, clientCertificate: null, cts.Token));
    }

    [Fact]
    public async Task ObserveServerFingerprint_NonHttpsProfile_ReturnsNullWithoutProbing()
    {
        var probe = new TlsServerCertificateProbe();
        var profile = ProfileFor(new Uri("http://dev-east.honua.example/"));

        var fingerprint = await probe.ObserveServerFingerprintAsync(profile, clientCertificate: null);

        Assert.Null(fingerprint);
    }

    private static ConsoleEnvironmentProfile ProfileFor(Uri serverBaseUri) => new()
    {
        Id = "probe-test",
        DisplayName = "Probe Test",
        ServerBaseUri = serverBaseUri,
        EnvironmentKind = "development",
        TenantId = "probe-test"
    };
}
