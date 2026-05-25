using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): The Console .NET environment/trust contracts
// (Honua.Sdk.Abstractions.Environments) are merged on honua-sdk-dotnet trunk but
// are NOT yet shipped in a consumable Honua.Sdk.Abstractions package (the latest
// published 1.0.0 predates #166). Per SDK_SHIM_POLICY.md these server-owned shapes
// live behind the single Console contract boundary, mirrored exactly, until the SDK
// package ships #166 and honua-console#7 swaps these for
// `global using HonuaCertificateValidationStatus = Honua.Sdk.Abstractions.Environments.HonuaCertificateValidationStatus;`
// (and the sibling types). Do not re-declare these outside this boundary file.

/// <summary>
/// Result of host-supplied certificate and trust validation.
/// Mirrors <c>Honua.Sdk.Abstractions.Environments.HonuaCertificateValidationStatus</c> (honua-sdk-dotnet#166).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HonuaCertificateValidationStatus>))]
public enum HonuaCertificateValidationStatus
{
    /// <summary>Validation has not been configured.</summary>
    NotConfigured,

    /// <summary>Required certificate reference could not be resolved.</summary>
    Missing,

    /// <summary>Certificate is expired.</summary>
    Expired,

    /// <summary>Certificate is valid but close to expiration.</summary>
    ExpiringSoon,

    /// <summary>Certificate or server trust chain is untrusted.</summary>
    Untrusted,

    /// <summary>Server rejected the client certificate.</summary>
    Rejected,

    /// <summary>Certificate is valid but bound to a different environment.</summary>
    WrongEnvironment,

    /// <summary>Trust validation succeeded and the profile is ready.</summary>
    Ready
}

/// <summary>
/// Last host-supplied trust validation state for an environment.
/// Mirrors <c>Honua.Sdk.Abstractions.Environments.HonuaEnvironmentTrustState</c> (honua-sdk-dotnet#166).
/// Secret paths, certificate bytes, and private-key ids must never be stored here.
/// </summary>
public sealed record HonuaEnvironmentTrustState
{
    /// <summary>Current validation status.</summary>
    public HonuaCertificateValidationStatus Status { get; init; } = HonuaCertificateValidationStatus.NotConfigured;

    /// <summary>Timestamp when the state was checked.</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>Certificate expiration timestamp, when known.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Safe server certificate fingerprint summary, when exposed by the host.</summary>
    public string? ServerFingerprint { get; init; }

    /// <summary>Safe issuer summary, when exposed by the host.</summary>
    public string? IssuerSummary { get; init; }

    /// <summary>Policy identifier used for validation.</summary>
    public string? PolicyId { get; init; }

    /// <summary>Sanitized machine-readable reason code.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Sanitized operator-facing message.</summary>
    public string? SanitizedMessage { get; init; }
}

// SHIM(honua-server#1171): wire contract for the closed server mTLS/trust API
// `POST /api/v1/admin/security/client-certificates/validate`. honua-server#1171 is
// merged on the server trunk but is not projected to honua-sdk-dotnet. These request,
// result, and envelope shapes mirror the server's ValidateClientCertificateRequest /
// ValidateClientCertificateResponse / ApiResponse<T>. Remove when the SDK projects the
// trust client (honua-console#7).

/// <summary>
/// Request payload for the server client-certificate validation endpoint.
/// </summary>
public sealed record ConsoleClientCertificateValidationRequest
{
    /// <summary>PEM or base64 DER public client certificate.</summary>
    [JsonPropertyName("certificate")]
    public required string Certificate { get; init; }

    /// <summary>Certificate encoding: pem, urlEncodedPem, or base64Der.</summary>
    [JsonPropertyName("encoding")]
    public string Encoding { get; init; } = "pem";

    /// <summary>Optional expected trust profile id.</summary>
    [JsonPropertyName("profileId")]
    public string? ProfileId { get; init; }
}

/// <summary>
/// Result of validating a public client certificate against configured server trust profiles.
/// </summary>
public sealed record ConsoleClientCertificateValidationResult
{
    /// <summary>Whether validation succeeded.</summary>
    [JsonPropertyName("valid")]
    public bool Valid { get; init; }

    /// <summary>Stable machine-readable validation code (e.g. <c>success</c>, <c>client_certificate_untrusted_issuer</c>).</summary>
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    /// <summary>Sanitized validation detail.</summary>
    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    /// <summary>Matched Honua principal id when validation succeeds.</summary>
    [JsonPropertyName("principalId")]
    public string? PrincipalId { get; init; }

    /// <summary>Matched trust profile id when available.</summary>
    [JsonPropertyName("trustProfileId")]
    public string? TrustProfileId { get; init; }

    /// <summary>Matched mapping id when available.</summary>
    [JsonPropertyName("mappingId")]
    public string? MappingId { get; init; }

    /// <summary>Matched Honua environment id when available.</summary>
    [JsonPropertyName("environmentId")]
    public string? EnvironmentId { get; init; }

    /// <summary>Certificate SHA-256 fingerprint.</summary>
    [JsonPropertyName("fingerprintSha256")]
    public string? FingerprintSha256 { get; init; }

    /// <summary>Days until certificate expiry when available.</summary>
    [JsonPropertyName("daysUntilExpiry")]
    public int? DaysUntilExpiry { get; init; }
}

/// <summary>
/// Minimal Honua server response envelope (<c>ApiResponse&lt;T&gt;</c>).
/// </summary>
public sealed record ConsoleServerEnvelope<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Stable validation codes returned by honua-server#1171 and their projection onto
/// <see cref="HonuaCertificateValidationStatus"/>.
/// </summary>
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

    /// <summary>Default number of days before expiry at which a valid certificate is surfaced as a warning.</summary>
    public const int DefaultExpiringSoonThresholdDays = 30;

    /// <summary>
    /// Projects a server validation result onto the host-neutral trust status. A successful
    /// validation whose certificate expires within <paramref name="expiringSoonThresholdDays"/>
    /// is surfaced as <see cref="HonuaCertificateValidationStatus.ExpiringSoon"/>.
    /// </summary>
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

    /// <summary>Whether a status refuses a native connection until acknowledged or revalidated.</summary>
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
