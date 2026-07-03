using Honua.Console.Contracts;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Real-server mTLS/trust integration assertions (AC#4). Boots a real honua-server + PostgreSQL via
/// <see cref="HonuaServerMtlsFixture"/> and drives the production trust validation client, server
/// certificate probe, and trust evaluator against live data. Every fact skips with a clear reason
/// when Docker, the server image, or the opt-in flag is unavailable.
/// </summary>
[Collection(ConsoleTrustIntegrationCollection.Name)]
public sealed class ConsoleTrustValidationIntegrationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private readonly HonuaServerMtlsFixture _fixture;

    public ConsoleTrustValidationIntegrationTests(HonuaServerMtlsFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task UntrustedClientCertificate_IsRejectedByLiveValidation()
    {
        SkipIf(ConsoleTrustIntegrationOptions.GetValidateSkipReason());

        var profile = _fixture.BuildProfile();
        var client = _fixture.CreateValidationClient();
        var probe = _fixture.CreateProbe();

        var observedFingerprint = await probe.ObserveServerFingerprintAsync(profile, _fixture.UntrustedClientCertificate);
        var result = await client.ValidateAsync(profile, _fixture.UntrustedClientCertificate, observedFingerprint);

        Assert.False(result.Valid);
        var status = ConsoleCertificateValidationCodes.ToStatus(result.Code, result.Valid, result.DaysUntilExpiry);
        Assert.True(ConsoleCertificateValidationCodes.IsBlocking(status), $"Expected a blocking status for code '{result.Code}'.");

        var evaluation = new ConsoleTrustEvaluator().Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = profile,
            State = new ConsoleEnvironmentState { ProfileId = profile.Id, PinnedServerFingerprint = observedFingerprint ?? string.Empty },
            ObservedServerFingerprint = observedFingerprint,
            ClientCertificateThumbprint = NativeServerTrust.ComputeSha256Thumbprint(_fixture.UntrustedClientCertificate),
            Validation = result,
            Now = Now
        });

        Assert.True(evaluation.IsBlocked);
    }

    [SkippableFact]
    public async Task ChangedServerFingerprint_BlocksUntilAcknowledged_AgainstLiveServer()
    {
        SkipIf(ConsoleTrustIntegrationOptions.GetSkipReason());
        // The change-detection assertions below pivot on a real, observed server-certificate fingerprint.
        // That requires a TLS handshake; when the live server boots plain HTTP (e.g. the pinned nightly
        // image) there is no certificate to fingerprint, so skip cleanly rather than fail on an empty
        // fingerprint. When a TLS-enabled server IS configured, the full assertion still runs.
        SkipIf(_fixture.ServerFingerprintProbeSkipReason);

        var profile = _fixture.BuildProfile();
        var probe = _fixture.CreateProbe();
        var evaluator = new ConsoleTrustEvaluator();

        var observedFingerprint = await probe.ObserveServerFingerprintAsync(profile, _fixture.UntrustedClientCertificate);
        Assert.False(string.IsNullOrEmpty(observedFingerprint), "Expected to observe a live server certificate fingerprint.");

        var pinnedState = new ConsoleEnvironmentState
        {
            ProfileId = profile.Id,
            PinnedServerFingerprint = observedFingerprint!
        };

        // Re-observing the same live identity does not block.
        var unchanged = evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = profile with { ClientCertificate = new ConsoleClientCertificateBinding() },
            State = pinnedState,
            ObservedServerFingerprint = observedFingerprint,
            Now = Now
        });
        Assert.False(unchanged.IsBlocked);

        // A different observed identity blocks the connection.
        const string changed = "0000000000000000000000000000000000000000000000000000000000000000";
        var blocked = evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = profile with { ClientCertificate = new ConsoleClientCertificateBinding() },
            State = pinnedState,
            ObservedServerFingerprint = changed,
            Now = Now
        });
        Assert.True(blocked.IsBlocked);
        Assert.Equal(ConsoleTrustReasonCodes.ServerCertificateChanged, blocked.Trust.ReasonCode);

        // Acknowledging re-pins the new identity and clears the block.
        var acknowledged = evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = profile with { ClientCertificate = new ConsoleClientCertificateBinding() },
            State = pinnedState,
            ObservedServerFingerprint = changed,
            Acknowledge = true,
            Now = Now
        });
        Assert.False(acknowledged.IsBlocked);
        Assert.Equal(changed, acknowledged.ServerFingerprintToPin);
    }

    [SkippableFact]
    public async Task TrustedClientCertificate_ValidatesReady()
    {
        SkipIf(ConsoleTrustIntegrationOptions.GetValidateSkipReason());

        using var trusted = _fixture.LoadTrustedCertificate();
        Skip.If(trusted is null, "Set HONUA_CONSOLE_TRUSTED_CERT_PFX to a server-trusted client certificate to assert the Ready path.");

        var profile = _fixture.BuildProfile();
        var client = _fixture.CreateValidationClient();
        var probe = _fixture.CreateProbe();

        var observedFingerprint = await probe.ObserveServerFingerprintAsync(profile, trusted);
        var result = await client.ValidateAsync(profile, trusted!, observedFingerprint);

        Assert.True(result.Valid, $"Expected the seeded certificate to validate; server returned '{result.Code}'.");
        var status = ConsoleCertificateValidationCodes.ToStatus(result.Code, result.Valid, result.DaysUntilExpiry);
        Assert.False(ConsoleCertificateValidationCodes.IsBlocking(status));
    }

    private static void SkipIf(string? reason) => Skip.If(reason is not null, reason ?? string.Empty);
}
