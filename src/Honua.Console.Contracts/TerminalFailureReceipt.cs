using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Honua.Console.Contracts;

public enum TerminalFailureKind
{
    Unknown,
    Authentication,
    Authorization,
    NotFound,
    Validation,
    Conflict,
    Throttled,
    Unavailable
}

public sealed record TerminalFieldFailure(
    string? Code,
    string? Severity,
    string? Path,
    string? FieldId,
    int? ItemIndex,
    string? Message);

public sealed record TerminalProtocolMetadata(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Initial,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Trailing);

public sealed record TerminalFailureReceipt(
    int? TransportStatus,
    string? ProtocolCode,
    TerminalFailureKind Kind,
    string Code,
    bool Retryable,
    double? RetryAfterSeconds,
    string? CorrelationId,
    IReadOnlyList<TerminalFieldFailure> FieldErrors,
    TerminalProtocolMetadata ProtocolMetadata);

/// <summary>Single parser used by every Console HTTP terminal-result boundary.</summary>
public static class ConsoleFailureReceiptParser
{
    private static readonly HashSet<string> SensitiveHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Authorization", "Cookie", "Set-Cookie", "X-API-Key" };

    public static TerminalFailureReceipt Parse(HttpResponseMessage response, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        JsonElement? root = ParseObject(body);
        JsonElement? source = root;
        if (root is { } rootValue && rootValue.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            source = error;
        }

        var status = (int)response.StatusCode;
        var kind = ParseKind(GetString(source, "kind")) ?? Classify(status);
        var code = GetString(source, "machineCode") ?? GetString(source, "code") ?? DefaultCode(kind);
        var retryable = GetBoolean(source, "retryable") ?? status is 408 or 429 or 500 or 502 or 503 or 504;
        var retryAfter = GetNumber(source, "retryAfterSeconds") ?? ParseRetryAfter(response);
        var correlationId = GetString(source, "correlationId") ?? GetString(root, "correlationId") ??
            Header(response, "X-Correlation-ID", "Honua-Request-Id", "X-Request-Id");

        return new TerminalFailureReceipt(
            status,
            null,
            kind,
            code,
            retryable,
            retryAfter,
            correlationId,
            ParseErrors(source, root),
            new TerminalProtocolMetadata(CopyHeaders(response), new Dictionary<string, IReadOnlyList<string>>()));
    }

    public static TerminalFailureReceipt FromStatus(HttpStatusCode statusCode) =>
        Parse(new HttpResponseMessage(statusCode));

    private static JsonElement? ParseObject(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<TerminalFieldFailure> ParseErrors(JsonElement? source, JsonElement? root)
    {
        JsonElement errors;
        if (source is { } sourceValue && sourceValue.TryGetProperty("errors", out errors))
        {
        }
        else if (root is { } rootValue && rootValue.TryGetProperty("errors", out errors))
        {
        }
        else
        {
            return [];
        }

        var result = new List<TerminalFieldFailure>();
        if (errors.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errors.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                result.Add(new TerminalFieldFailure(
                    GetString(item, "code"),
                    GetString(item, "severity"),
                    GetString(item, "path"),
                    GetString(item, "fieldId"),
                    GetInt32(item, "itemIndex"),
                    GetString(item, "message")));
            }
        }
        else if (errors.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in errors.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array) continue;
                result.AddRange(property.Value.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => new TerminalFieldFailure(null, null, property.Name, property.Name, null, value.GetString())));
            }
        }

        return result;
    }

    private static Dictionary<string, IReadOnlyList<string>> CopyHeaders(HttpResponseMessage response)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in response.Headers.Concat(response.Content.Headers))
        {
            if (!SensitiveHeaders.Contains(pair.Key)) result[pair.Key] = pair.Value.ToArray();
        }
        return result;
    }

    private static string? Header(HttpResponseMessage response, params string[] names)
    {
        foreach (var name in names)
        {
            if (response.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } value)
                return value;
        }
        return null;
    }

    private static double? ParseRetryAfter(HttpResponseMessage response)
    {
        var value = response.Headers.RetryAfter;
        if (value?.Delta is { } delta) return Math.Max(0, delta.TotalSeconds);
        if (value?.Date is { } date) return Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds);
        return null;
    }

    private static string? GetString(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value &&
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? GetString(JsonElement element, string name) => GetString((JsonElement?)element, name);
    private static bool? GetBoolean(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var property) &&
        property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
    private static double? GetNumber(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number) ? number : null;
    private static int? GetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var number) ? number : null;

    private static TerminalFailureKind? ParseKind(string? kind) => kind switch
    {
        "authentication" => TerminalFailureKind.Authentication,
        "authorization" => TerminalFailureKind.Authorization,
        "not-found" => TerminalFailureKind.NotFound,
        "validation" => TerminalFailureKind.Validation,
        "conflict" => TerminalFailureKind.Conflict,
        "throttled" => TerminalFailureKind.Throttled,
        "unavailable" => TerminalFailureKind.Unavailable,
        "unknown" => TerminalFailureKind.Unknown,
        _ => null
    };

    private static TerminalFailureKind Classify(int status) => status switch
    {
        401 => TerminalFailureKind.Authentication,
        403 => TerminalFailureKind.Authorization,
        404 => TerminalFailureKind.NotFound,
        400 or 422 => TerminalFailureKind.Validation,
        409 or 412 or 428 => TerminalFailureKind.Conflict,
        429 => TerminalFailureKind.Throttled,
        408 or 500 or 502 or 503 or 504 => TerminalFailureKind.Unavailable,
        _ => TerminalFailureKind.Unknown
    };

    private static string DefaultCode(TerminalFailureKind kind) => kind switch
    {
        TerminalFailureKind.Authentication => "authentication_required",
        TerminalFailureKind.Authorization => "permission_denied",
        TerminalFailureKind.NotFound => "resource_not_found",
        TerminalFailureKind.Validation => "validation_failed",
        TerminalFailureKind.Conflict => "resource_conflict",
        TerminalFailureKind.Throttled => "rate_limited",
        TerminalFailureKind.Unavailable => "service_unavailable",
        _ => "unknown_failure"
    };
}
