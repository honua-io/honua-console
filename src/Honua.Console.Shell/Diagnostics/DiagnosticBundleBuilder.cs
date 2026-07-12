using System.Globalization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Diagnostics;

/// <summary>
/// A raw, potentially-unsafe captured HTTP exchange as Console observes it (headers and bodies
/// may carry secrets). It is never serialized directly; <see cref="DiagnosticBundleBuilder"/>
/// sanitizes it into a <see cref="DiagnosticEnvelope"/> before it can enter a bundle.
/// </summary>
public sealed record DiagnosticExchangeCapture
{
    public string Method { get; init; } = string.Empty;

    /// <summary>Raw request path (and optional query); query values are placeholdered on sanitize.</summary>
    public string PathAndQuery { get; init; } = string.Empty;

    public int? StatusCode { get; init; }

    public string? MediaType { get; init; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>Raw request headers (may include Authorization/Cookie; dropped on sanitize).</summary>
    public IReadOnlyList<DiagnosticHeader>? RequestHeaders { get; init; }

    /// <summary>Raw response headers (may include Set-Cookie; dropped on sanitize).</summary>
    public IReadOnlyList<DiagnosticHeader>? ResponseHeaders { get; init; }

    /// <summary>Raw request body text (may include secrets; redacted + truncated on sanitize).</summary>
    public string? RequestBody { get; init; }

    /// <summary>Raw response body text (may include secrets; redacted + truncated on sanitize).</summary>
    public string? ResponseBody { get; init; }
}

/// <summary>
/// Builds a canonical <see cref="DiagnosticBundle"/> from raw captured exchanges, running every
/// exchange through <see cref="DiagnosticBundleSanitizer"/> so the resulting bundle carries no
/// raw bytes, auth headers, cookies, or secrets (honua-console#307). The builder shapes the
/// bundle; <see cref="DiagnosticBundleExporter"/> is the gate that validates it against the schema
/// before it can be downloaded or uploaded.
/// </summary>
public static class DiagnosticBundleBuilder
{
    public static DiagnosticBundle Build(
        DiagnosticConsent consent,
        string contentClassification,
        IEnumerable<DiagnosticExchangeCapture> captures,
        string? bundleId = null)
    {
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(captures);

        List<DiagnosticEnvelope> envelopes = [.. captures.Select(ToEnvelope)];

        return new DiagnosticBundle
        {
            SchemaVersion = "1.0",
            BundleId = string.IsNullOrWhiteSpace(bundleId) ? null : bundleId.Trim(),
            ContentClassification = string.IsNullOrWhiteSpace(contentClassification)
                ? DiagnosticContentClassification.Unknown
                : contentClassification.Trim(),
            Consent = consent,
            Envelopes = envelopes,
        };
    }

    private static DiagnosticEnvelope ToEnvelope(DiagnosticExchangeCapture capture) => new()
    {
        Method = (capture.Method ?? string.Empty).Trim(),
        NormalizedPath = DiagnosticBundleSanitizer.NormalizePath(capture.PathAndQuery),
        StatusCode = capture.StatusCode,
        MediaType = Trimmed(capture.MediaType),
        CorrelationId = Trimmed(capture.CorrelationId),
        TraceId = Trimmed(capture.TraceId),
        CapturedAt = capture.CapturedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        RequestHeaders = DiagnosticBundleSanitizer.SanitizeHeaders(capture.RequestHeaders),
        ResponseHeaders = DiagnosticBundleSanitizer.SanitizeHeaders(capture.ResponseHeaders),
        RequestBody = DiagnosticBundleSanitizer.RedactBody(capture.RequestBody),
        ResponseBody = DiagnosticBundleSanitizer.RedactBody(capture.ResponseBody),
    };

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
