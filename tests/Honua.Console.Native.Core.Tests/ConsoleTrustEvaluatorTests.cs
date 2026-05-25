using Honua.Console.Contracts;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleTrustEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly ConsoleTrustEvaluator _evaluator = new();

    [Fact]
    public void FirstUse_PinsObservedServerFingerprint_AndDoesNotBlock()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: false),
            State = State(),
            ObservedServerFingerprint = "AAA",
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal("AAA", result.ServerFingerprintToPin);
        Assert.Equal("AAA", result.Trust.ServerFingerprint);
    }

    [Fact]
    public void ChangedServerFingerprint_BlocksConnection_AndHoldsPin()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: false),
            State = State(pinnedServer: "AAA"),
            ObservedServerFingerprint = "BBB",
            Now = Now
        });

        Assert.True(result.IsBlocked);
        Assert.Equal(ConsoleTrustReasonCodes.ServerCertificateChanged, result.Trust.ReasonCode);
        Assert.Equal(HonuaCertificateValidationStatus.Untrusted, result.Trust.Status);
        Assert.Equal("AAA", result.ServerFingerprintToPin);
        Assert.Equal("BBB", result.Trust.ServerFingerprint);
    }

    [Fact]
    public void Acknowledge_AfterServerChange_RePinsObservedFingerprint_AndClearsBlock()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: false),
            State = State(pinnedServer: "AAA"),
            ObservedServerFingerprint = "BBB",
            Acknowledge = true,
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal("BBB", result.ServerFingerprintToPin);
    }

    [Fact]
    public void UnchangedServerFingerprint_DoesNotBlock()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: false),
            State = State(pinnedServer: "AAA"),
            ObservedServerFingerprint = "AAA",
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal("AAA", result.ServerFingerprintToPin);
    }

    [Fact]
    public void ChangedClientCertificate_BlocksConnection()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "OLDTHUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "NEWTHUMB",
            Validation = Validation(ConsoleCertificateValidationCodes.Success, valid: true),
            Now = Now
        });

        Assert.True(result.IsBlocked);
        Assert.Equal(ConsoleTrustReasonCodes.ClientCertificateChanged, result.Trust.ReasonCode);
        Assert.Equal("OLDTHUMB", result.ClientThumbprintToPin);
    }

    [Fact]
    public void ChangedClientCertificate_RevalidationWithReadyResult_RePinsAndClearsBlock()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "OLDTHUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "NEWTHUMB",
            Validation = Validation(ConsoleCertificateValidationCodes.Success, valid: true),
            RevalidateClientCertificateChange = true,
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.Ready, result.Trust.Status);
        Assert.Equal("NEWTHUMB", result.ClientThumbprintToPin);
    }

    [Fact]
    public void ChangedClientCertificate_RevalidationWithRejectedResult_StaysBlocked()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "OLDTHUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "NEWTHUMB",
            Validation = Validation(ConsoleCertificateValidationCodes.UnmappedIdentity, valid: false),
            RevalidateClientCertificateChange = true,
            Now = Now
        });

        Assert.True(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.Rejected, result.Trust.Status);
        Assert.Equal("OLDTHUMB", result.ClientThumbprintToPin);
    }

    [Fact]
    public void MissingRequiredClientCertificate_BlocksConnection()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA"),
            ObservedServerFingerprint = "AAA",
            Now = Now
        });

        Assert.True(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.Missing, result.Trust.Status);
        Assert.Equal(ConsoleCertificateValidationCodes.Missing, result.Trust.ReasonCode);
    }

    [Fact]
    public void ValidCertificate_PinsClientThumbprint_AndReportsReady()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "THUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "THUMB",
            Validation = Validation(ConsoleCertificateValidationCodes.Success, valid: true),
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.Ready, result.Trust.Status);
        Assert.Equal("THUMB", result.ClientThumbprintToPin);
    }

    [Theory]
    [InlineData(ConsoleCertificateValidationCodes.UntrustedIssuer, HonuaCertificateValidationStatus.Untrusted)]
    [InlineData(ConsoleCertificateValidationCodes.UntrustedChain, HonuaCertificateValidationStatus.Untrusted)]
    [InlineData(ConsoleCertificateValidationCodes.Revoked, HonuaCertificateValidationStatus.Rejected)]
    [InlineData(ConsoleCertificateValidationCodes.WrongEnvironment, HonuaCertificateValidationStatus.WrongEnvironment)]
    [InlineData(ConsoleCertificateValidationCodes.Expired, HonuaCertificateValidationStatus.Expired)]
    [InlineData(ConsoleCertificateValidationCodes.Missing, HonuaCertificateValidationStatus.Missing)]
    public void FailingValidation_Blocks_WithMappedStatus(string code, HonuaCertificateValidationStatus expected)
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "THUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "THUMB",
            Validation = Validation(code, valid: false),
            Now = Now
        });

        Assert.True(result.IsBlocked);
        Assert.Equal(expected, result.Trust.Status);
        Assert.Equal(code, result.Trust.ReasonCode);
    }

    [Fact]
    public void ValidCertificateNearExpiry_WarnsExpiringSoon_WithoutBlocking()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: true),
            State = State(pinnedServer: "AAA", pinnedClient: "THUMB"),
            ObservedServerFingerprint = "AAA",
            ClientCertificateThumbprint = "THUMB",
            Validation = Validation(ConsoleCertificateValidationCodes.Success, valid: true, daysUntilExpiry: 7),
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.ExpiringSoon, result.Trust.Status);
    }

    [Fact]
    public void NoClientCertificate_ReportsNotConfigured()
    {
        var result = _evaluator.Evaluate(new ConsoleTrustEvaluationContext
        {
            Profile = Profile(certEnabled: false),
            State = State(pinnedServer: "AAA"),
            ObservedServerFingerprint = "AAA",
            Now = Now
        });

        Assert.False(result.IsBlocked);
        Assert.Equal(HonuaCertificateValidationStatus.NotConfigured, result.Trust.Status);
    }

    [Theory]
    [InlineData(ConsoleCertificateValidationCodes.Success, true, null, HonuaCertificateValidationStatus.Ready)]
    [InlineData(ConsoleCertificateValidationCodes.Success, true, 5, HonuaCertificateValidationStatus.ExpiringSoon)]
    [InlineData(ConsoleCertificateValidationCodes.Expired, false, null, HonuaCertificateValidationStatus.Expired)]
    [InlineData(ConsoleCertificateValidationCodes.Revoked, false, null, HonuaCertificateValidationStatus.Rejected)]
    [InlineData("client_certificate_unknown_future_code", false, null, HonuaCertificateValidationStatus.Untrusted)]
    public void ToStatus_MapsStableCodes(string code, bool valid, int? days, HonuaCertificateValidationStatus expected)
    {
        Assert.Equal(expected, ConsoleCertificateValidationCodes.ToStatus(code, valid, days));
    }

    private static ConsoleEnvironmentProfile Profile(bool certEnabled) => new()
    {
        Id = "staging",
        DisplayName = "Staging",
        ServerBaseUri = new Uri("https://staging.honua.example"),
        TenantId = "honua-staging",
        ClientCertificate = certEnabled
            ? new ConsoleClientCertificateBinding
            {
                Enabled = true,
                Reference = new ConsoleClientCertificateReference
                {
                    Kind = ConsoleClientCertificateReferenceKind.StoreThumbprint,
                    Value = "THUMB"
                }
            }
            : new ConsoleClientCertificateBinding()
    };

    private static ConsoleEnvironmentState State(string pinnedServer = "", string pinnedClient = "") => new()
    {
        ProfileId = "staging",
        PinnedServerFingerprint = pinnedServer,
        PinnedClientCertificateThumbprint = pinnedClient
    };

    private static ConsoleClientCertificateValidationResult Validation(string code, bool valid, int? daysUntilExpiry = null) => new()
    {
        Valid = valid,
        Code = code,
        Detail = "Sanitized detail.",
        DaysUntilExpiry = daysUntilExpiry
    };
}
