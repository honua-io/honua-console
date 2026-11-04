using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<HonuaCertificateValidationStatus>))]
public enum HonuaCertificateValidationStatus
{
    NotConfigured,

    Missing,

    Expired,

    ExpiringSoon,

    Untrusted,

    Rejected,

    WrongEnvironment,

    Ready
}

// Do not store secret paths, certificate bytes, or private-key identifiers here.
public sealed record HonuaEnvironmentTrustState
{
    public HonuaCertificateValidationStatus Status { get; init; } = HonuaCertificateValidationStatus.NotConfigured;

    public DateTimeOffset? CheckedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? ServerFingerprint { get; init; }

    public string? IssuerSummary { get; init; }

    public string? PolicyId { get; init; }

    public string? ReasonCode { get; init; }

    public string? SanitizedMessage { get; init; }
}

public sealed record ConsoleClientCertificateValidationRequest
{
    // The certificate is public PEM or base64 DER, never a private key.
    [JsonPropertyName("certificate")]
    public required string Certificate { get; init; }

    // Accepted values are pem, urlEncodedPem, and base64Der.
    [JsonPropertyName("encoding")]
    public string Encoding { get; init; } = "pem";

    [JsonPropertyName("profileId")]
    public string? ProfileId { get; init; }
}

public sealed record ConsoleClientCertificateValidationResult
{
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("principalId")]
    public string? PrincipalId { get; init; }

    [JsonPropertyName("trustProfileId")]
    public string? TrustProfileId { get; init; }

    [JsonPropertyName("mappingId")]
    public string? MappingId { get; init; }

    [JsonPropertyName("environmentId")]
    public string? EnvironmentId { get; init; }

    [JsonPropertyName("fingerprintSha256")]
    public string? FingerprintSha256 { get; init; }

    [JsonPropertyName("daysUntilExpiry")]
    public int? DaysUntilExpiry { get; init; }
}

public sealed record ConsoleServerEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public static class ConsoleCertificateValidationCodes
{
    public const string Success = "success";
    public const string Missing = "client_certificate_missing";
    public const string Expired = "client_certificate_expired";
    public const string NotYetValid = "client_certificate_not_yet_valid";
    public const string UntrustedIssuer = "client_certificate_untrusted_issuer";
    public const string UntrustedChain = "client_certificate_untrusted_chain";
    public const string Revoked = "client_certificate_revoked";
    public const string WrongEnvironment = "client_certificate_wrong_environment";
    public const string MissingIdentity = "client_certificate_missing_identity";
    public const string UnmappedIdentity = "client_certificate_unmapped_identity";
    public const string InvalidEku = "client_certificate_invalid_eku";
    public const string ForwardingUntrusted = "client_certificate_forwarding_untrusted";
    public const string ForwardingInvalid = "client_certificate_forwarding_invalid";
    public const string InsufficientRbac = "client_certificate_insufficient_rbac";

    public const int DefaultExpiringSoonThresholdDays = 30;

    public static HonuaCertificateValidationStatus ToStatus(
        string? code,
        bool valid,
        int? daysUntilExpiry,
        int expiringSoonThresholdDays = DefaultExpiringSoonThresholdDays)
    {
        if (valid || string.Equals(code, Success, StringComparison.OrdinalIgnoreCase))
        {
            return daysUntilExpiry is { } days && days <= expiringSoonThresholdDays
                ? HonuaCertificateValidationStatus.ExpiringSoon
                : HonuaCertificateValidationStatus.Ready;
        }

        return code switch
        {
            Expired => HonuaCertificateValidationStatus.Expired,
            WrongEnvironment => HonuaCertificateValidationStatus.WrongEnvironment,
            Missing or MissingIdentity => HonuaCertificateValidationStatus.Missing,
            UntrustedIssuer or UntrustedChain or NotYetValid => HonuaCertificateValidationStatus.Untrusted,
            Revoked or UnmappedIdentity or InvalidEku or InsufficientRbac
                or ForwardingUntrusted or ForwardingInvalid => HonuaCertificateValidationStatus.Rejected,
            _ => HonuaCertificateValidationStatus.Untrusted
        };
    }

    public static bool IsBlocking(HonuaCertificateValidationStatus status) => status switch
    {
        HonuaCertificateValidationStatus.Untrusted
            or HonuaCertificateValidationStatus.Rejected
            or HonuaCertificateValidationStatus.WrongEnvironment
            or HonuaCertificateValidationStatus.Expired
            or HonuaCertificateValidationStatus.Missing => true,
        _ => false
    };
}
