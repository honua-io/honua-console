using System.Globalization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Diagnostics;

// Raw capture. Sanitize before it enters a bundle; these fields can hold secrets.
public sealed record DiagnosticExchangeCapture
{
    public string Method { get; init; } = string.Empty;

    public string PathAndQuery { get; init; } = string.Empty;

    public int? StatusCode { get; init; }

    public string? MediaType { get; init; }

    public string? CorrelationId { get; init; }

    public string? TraceId { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    public IReadOnlyList<DiagnosticHeader>? RequestHeaders { get; init; }

    public IReadOnlyList<DiagnosticHeader>? ResponseHeaders { get; init; }

    public string? RequestBody { get; init; }

    public string? ResponseBody { get; init; }
}

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
