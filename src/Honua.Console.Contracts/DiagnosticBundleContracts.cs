using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public static class DiagnosticContentClassification
{
    public const string Unknown = "unknown";
    public const string Public = "public";
    public const string Internal = "internal";
    public const string CustomerData = "customer-data";
    public const string SecretSuspected = "secret-suspected";
}

// A bundle never includes raw request or response bytes, or secret headers.
public sealed record DiagnosticBundle
{
    public string SchemaVersion { get; init; } = "1.0";

    public string? BundleId { get; init; }

    public string ContentClassification { get; init; } = DiagnosticContentClassification.Unknown;

    public DiagnosticConsent Consent { get; init; } = new();

    public IReadOnlyList<DiagnosticEnvelope> Envelopes { get; init; } = [];
}

public sealed record DiagnosticConsent
{
    public bool RedactionAcknowledged { get; init; }

    public bool ShareWithSupport { get; init; }

    public string? GrantedBy { get; init; }
}

public sealed record DiagnosticEnvelope
{
    public string Method { get; init; } = string.Empty;

    // Path parameters and query values are placeholders, not the captured text.
    public string NormalizedPath { get; init; } = string.Empty;

    public int? StatusCode { get; init; }

    public string? MediaType { get; init; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public string? CapturedAt { get; init; }

    // Authorization and Cookie are never included.
    public IReadOnlyList<DiagnosticHeader>? RequestHeaders { get; init; }

    // Set-Cookie is never included.
    public IReadOnlyList<DiagnosticHeader>? ResponseHeaders { get; init; }

    public DiagnosticBodyPreview? RequestBody { get; init; }

    public DiagnosticBodyPreview? ResponseBody { get; init; }
}

public sealed record DiagnosticHeader(string Name, string Value);

public sealed record DiagnosticBodyPreview
{
    public string? Preview { get; init; }

    // Hash of the original body bytes, taken before redaction or truncation.
    public string? ContentSha256 { get; init; }

    public long OriginalByteSize { get; init; }

    public bool RedactionApplied { get; init; }

    public bool Truncated { get; init; }
}

// The diagnostic-bundle schema forbids explicit null, so an absent optional is omitted.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DiagnosticBundle))]
[JsonSerializable(typeof(DiagnosticEnvelope))]
[JsonSerializable(typeof(DiagnosticConsent))]
[JsonSerializable(typeof(DiagnosticHeader))]
[JsonSerializable(typeof(DiagnosticBodyPreview))]
public sealed partial class DiagnosticBundleJsonContext : JsonSerializerContext;
