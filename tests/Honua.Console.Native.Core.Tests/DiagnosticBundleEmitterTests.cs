using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Diagnostics;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The Console diagnostic-bundle emitter: every emitted bundle validates against the canonical
/// schema before download/upload, absent optional fields are omitted (never explicit null), and
/// raw bytes / Authorization / Cookie / secrets never reach the wire (honua-console#307).
/// </summary>
public sealed class DiagnosticBundleEmitterTests
{
    private static readonly DiagnosticConsent Consent = new()
    {
        RedactionAcknowledged = true,
        ShareWithSupport = true,
    };

    private static readonly DiagnosticBundleExporter Exporter = new();

    [Fact]
    public void Emits_SchemaValidBundle_FromCapturedExchange()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent,
            DiagnosticContentClassification.Internal,
            [
                new DiagnosticExchangeCapture
                {
                    Method = "GET",
                    PathAndQuery = "/rest/services/{service}/FeatureServer/{layer}/query",
                    StatusCode = 200,
                    MediaType = "application/json",
                    CorrelationId = "corr-1",
                    CapturedAt = DateTimeOffset.UtcNow,
                    RequestHeaders = [new DiagnosticHeader("Content-Type", "application/json")],
                },
            ]);

        // Export both validates and returns bytes; no throw means schema-valid.
        string json = Exporter.ExportToJson(bundle);
        Assert.Empty(Exporter.Validate(bundle));
        Assert.Contains("\"schemaVersion\":\"1.0\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void OmitsAbsentOptionalFields_NeverEmittingExplicitNull()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent,
            DiagnosticContentClassification.Internal,
            [
                new DiagnosticExchangeCapture
                {
                    Method = "GET",
                    PathAndQuery = "/healthz/ready",
                },
            ]);

        string json = Exporter.ExportToJson(bundle);
        using JsonDocument document = JsonDocument.Parse(json);

        // No property anywhere is emitted as explicit null.
        Assert.False(HasNull(document.RootElement), "Emitted bundle contains an explicit null value.");

        // Absent optionals are simply absent, not present-and-null.
        Assert.False(document.RootElement.TryGetProperty("bundleId", out _));
        JsonElement envelope = document.RootElement.GetProperty("envelopes")[0];
        Assert.False(envelope.TryGetProperty("statusCode", out _));
        Assert.False(envelope.TryGetProperty("requestHeaders", out _));
        Assert.False(envelope.TryGetProperty("requestBody", out _));
    }

    [Fact]
    public void EmitsBundleId_WhenProvided_ButOmitsWhenBlank()
    {
        DiagnosticBundle withId = DiagnosticBundleBuilder.Build(
            Consent, DiagnosticContentClassification.Internal,
            [new DiagnosticExchangeCapture { Method = "GET", PathAndQuery = "/healthz" }],
            bundleId: "doctor-123");
        DiagnosticBundle withoutId = DiagnosticBundleBuilder.Build(
            Consent, DiagnosticContentClassification.Internal,
            [new DiagnosticExchangeCapture { Method = "GET", PathAndQuery = "/healthz" }],
            bundleId: "   ");

        Assert.Contains("\"bundleId\":\"doctor-123\"", Exporter.ExportToJson(withId), StringComparison.Ordinal);
        Assert.DoesNotContain("bundleId", Exporter.ExportToJson(withoutId), StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitizer_DropsAuthorizationCookieAndUnknownHeaders_KeepsAllowlisted()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent,
            DiagnosticContentClassification.CustomerData,
            [
                new DiagnosticExchangeCapture
                {
                    Method = "POST",
                    PathAndQuery = "/api/v1/tickets",
                    StatusCode = 401,
                    RequestHeaders =
                    [
                        new DiagnosticHeader("Authorization", "Bearer super-secret-token"),
                        new DiagnosticHeader("Cookie", "session=abc123"),
                        new DiagnosticHeader("X-API-Key", "ak_live_should_never_appear"),
                        new DiagnosticHeader("Content-Type", "application/json"),
                    ],
                    ResponseHeaders =
                    [
                        new DiagnosticHeader("Set-Cookie", "session=def456; HttpOnly"),
                        new DiagnosticHeader("Content-Type", "application/problem+json"),
                    ],
                },
            ]);

        DiagnosticEnvelope envelope = bundle.Envelopes[0];
        Assert.NotNull(envelope.RequestHeaders);
        Assert.Single(envelope.RequestHeaders!);
        Assert.Equal("Content-Type", envelope.RequestHeaders![0].Name);
        Assert.Single(envelope.ResponseHeaders!);
        Assert.Equal("Content-Type", envelope.ResponseHeaders![0].Name);

        string json = Exporter.ExportToJson(bundle);
        Assert.DoesNotContain("super-secret-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ak_live_should_never_appear", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session=", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitizer_RedactsBodySecrets_RecordsIntegrity_AndOmitsRawBytes()
    {
        const string rawBody =
            "{\"user\":\"alex\",\"password\":\"hunter2-super-secret\",\"apiKey\":\"ak_live_51H\"}";

        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent,
            DiagnosticContentClassification.SecretSuspected,
            [
                new DiagnosticExchangeCapture
                {
                    Method = "POST",
                    PathAndQuery = "/api/v1/login",
                    RequestBody = rawBody,
                },
            ]);

        DiagnosticBodyPreview preview = bundle.Envelopes[0].RequestBody!;
        Assert.True(preview.RedactionApplied);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(rawBody), preview.OriginalByteSize);
        Assert.False(string.IsNullOrEmpty(preview.ContentSha256));
        Assert.Equal(64, preview.ContentSha256!.Length);

        string json = Exporter.ExportToJson(bundle);
        Assert.DoesNotContain("hunter2-super-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ak_live_51H", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitizer_PlaceholdersQueryValues_SoTokensNeverLeakIntoPath()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent,
            DiagnosticContentClassification.Internal,
            [
                new DiagnosticExchangeCapture
                {
                    Method = "GET",
                    PathAndQuery = "/api/v1/tickets?token=secret-abc&status=open",
                },
            ]);

        string normalized = bundle.Envelopes[0].NormalizedPath;
        Assert.Equal("/api/v1/tickets?token={value}&status={value}", normalized);
        Assert.DoesNotContain("secret-abc", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_BlocksBundle_WithNoEnvelopes()
    {
        DiagnosticBundle empty = DiagnosticBundleBuilder.Build(
            Consent, DiagnosticContentClassification.Internal, []);

        DiagnosticBundleValidationException ex =
            Assert.Throws<DiagnosticBundleValidationException>(() => Exporter.Export(empty));
        Assert.NotEmpty(ex.Errors);
        Assert.Contains("diagnostic-bundle.v1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_BlocksBundle_WithInvalidContentClassification()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent, "totally-made-up",
            [new DiagnosticExchangeCapture { Method = "GET", PathAndQuery = "/healthz" }]);

        Assert.Throws<DiagnosticBundleValidationException>(() => Exporter.Export(bundle));
    }

    [Fact]
    public void Export_BlocksBundle_WhenStatusCodeOutOfRange()
    {
        DiagnosticBundle bundle = DiagnosticBundleBuilder.Build(
            Consent, DiagnosticContentClassification.Internal,
            [new DiagnosticExchangeCapture { Method = "GET", PathAndQuery = "/healthz", StatusCode = 600 }]);

        DiagnosticBundleValidationException ex =
            Assert.Throws<DiagnosticBundleValidationException>(() => Exporter.Export(bundle));
        Assert.Contains(ex.Errors, error => error.Contains("exceeds maximum 599", StringComparison.Ordinal));
    }

    private static bool HasNull(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return true;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (HasNull(property.Value))
                        return true;
                }

                return false;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (HasNull(item))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }
}
