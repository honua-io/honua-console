using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// Canonical sanitized diagnostic-bundle v1 contract (honua-console#307).
//
// These records are the Console-side projection of the support-owned canonical
// schema `https://honua.io/schemas/diagnostic-bundle.v1.json` (source of truth:
// honua-io/honua-support, honua-support#54/#57). Console does NOT fork the schema:
// the pinned schema bytes live under contracts/diagnostics/ and every emitted
// bundle is validated against them before download/upload
// (DiagnosticBundleExporter). This projection only needs to serialize to the same
// shape; the schema — not this type — is the authoritative gate.
//
// A diagnostic bundle NEVER carries raw request/response bytes, Authorization or
// Cookie headers, Set-Cookie, or any secret. Each captured HTTP exchange is a
// sanitized envelope: method, a normalized path (path params + query values
// placeholdered), status, media type, correlation/trace ids, an allowlist of safe
// headers ONLY, and redacted + truncated body previews with the ORIGINAL byte size
// and a content hash. On the wire the JSON is camelCase and every optional field is
// omitted when absent (never emitted as explicit null — the schema forbids null on
// its typed optionals).

/// <summary>Well-known <see cref="DiagnosticBundle.ContentClassification"/> values (schema enum).</summary>
public static class DiagnosticContentClassification
{
    public const string Unknown = "unknown";
    public const string Public = "public";
    public const string Internal = "internal";
    public const string CustomerData = "customer-data";
    public const string SecretSuspected = "secret-suspected";
}

/// <summary>
/// Canonical sanitized diagnostic bundle (<c>schemaVersion</c> "1.0"). Mirrors the
/// public <c>diagnostic-bundle.v1.json</c> contract. Optional fields are nullable so
/// they are omitted (never explicit <c>null</c>) when absent.
/// </summary>
public sealed record DiagnosticBundle
{
    /// <summary>Contract version. Always "1.0" for v1; additive fields keep v1.</summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>Optional client-assigned id. The server assigns its own id on intake.</summary>
    public string? BundleId { get; init; }

    /// <summary>Declared sensitivity after client-side redaction (see <see cref="DiagnosticContentClassification"/>).</summary>
    public string ContentClassification { get; init; } = DiagnosticContentClassification.Unknown;

    /// <summary>Explicit consent to share the sanitized bundle with Honua support.</summary>
    public DiagnosticConsent Consent { get; init; } = new();

    /// <summary>Sanitized HTTP exchanges. At least one; capped at 50.</summary>
    public IReadOnlyList<DiagnosticEnvelope> Envelopes { get; init; } = [];
}

/// <summary>Explicit consent block. <see cref="GrantedBy"/> is optional.</summary>
public sealed record DiagnosticConsent
{
    /// <summary>The submitter acknowledges the bundle was redacted and carries no raw secrets.</summary>
    public bool RedactionAcknowledged { get; init; }

    /// <summary>The submitter consents to sharing the sanitized bundle with Honua support.</summary>
    public bool ShareWithSupport { get; init; }

    /// <summary>Optional identity of the human/automation that granted consent.</summary>
    public string? GrantedBy { get; init; }
}

/// <summary>
/// One sanitized HTTP exchange. Only <see cref="Method"/> and <see cref="NormalizedPath"/>
/// are required; every other field is omitted when absent.
/// </summary>
public sealed record DiagnosticEnvelope
{
    /// <summary>HTTP method (e.g. GET, POST).</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Request path with path params and query values placeholdered.</summary>
    public string NormalizedPath { get; init; } = string.Empty;

    /// <summary>HTTP status code, when the exchange produced a response.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Response media type (e.g. application/json).</summary>
    public string? MediaType { get; init; }

    /// <summary>Correlation id linking the exchange to server logs.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Distributed-trace id for the exchange.</summary>
    public string? TraceId { get; init; }

    /// <summary>When the exchange was captured (ISO-8601).</summary>
    public string? CapturedAt { get; init; }

    /// <summary>Allowlisted, non-secret request headers ONLY. Authorization/Cookie are never present.</summary>
    public IReadOnlyList<DiagnosticHeader>? RequestHeaders { get; init; }

    /// <summary>Allowlisted, non-secret response headers ONLY. Set-Cookie is never present.</summary>
    public IReadOnlyList<DiagnosticHeader>? ResponseHeaders { get; init; }

    /// <summary>Redacted, truncated request body preview + integrity metadata.</summary>
    public DiagnosticBodyPreview? RequestBody { get; init; }

    /// <summary>Redacted, truncated response body preview + integrity metadata.</summary>
    public DiagnosticBodyPreview? ResponseBody { get; init; }
}

/// <summary>One allowlisted, non-secret header name/value pair.</summary>
public sealed record DiagnosticHeader(string Name, string Value);

/// <summary>
/// A redacted, truncated preview of a body plus integrity metadata for the ORIGINAL
/// bytes. The raw bytes are never included.
/// </summary>
public sealed record DiagnosticBodyPreview
{
    /// <summary>Redacted, truncated text preview of the body.</summary>
    public string? Preview { get; init; }

    /// <summary>Lowercase hex SHA-256 of the ORIGINAL (pre-redaction) body bytes.</summary>
    public string? ContentSha256 { get; init; }

    /// <summary>Size in bytes of the ORIGINAL body before truncation/redaction.</summary>
    public long OriginalByteSize { get; init; }

    /// <summary>True when redaction removed content from the preview.</summary>
    public bool RedactionApplied { get; init; }

    /// <summary>True when the preview is shorter than the original body.</summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Source-generated, AOT-safe serialization for the diagnostic-bundle contracts.
/// camelCase on the wire; optional properties are omitted when null so an absent
/// optional is never emitted as explicit <c>null</c> (the schema forbids it).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagnosticBundle))]
[JsonSerializable(typeof(DiagnosticEnvelope))]
[JsonSerializable(typeof(DiagnosticConsent))]
[JsonSerializable(typeof(DiagnosticHeader))]
[JsonSerializable(typeof(DiagnosticBodyPreview))]
public sealed partial class DiagnosticBundleJsonContext : JsonSerializerContext;
