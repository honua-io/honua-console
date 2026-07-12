using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

/// <summary>
/// Shared HTTP plumbing for the Studio natural-language generation clients (honua-console#279 PA-240).
/// <c>HttpStudioAppGenerationClient</c> and <c>HttpStudioMapGenerationClient</c> carried byte-identical
/// <c>SendAsync</c> / <c>TryReadErrorDetailAsync</c> / <c>CreateIssue</c> / JSON-options logic that differed
/// only by the "app"/"map" noun in the error text — the same copy-paste-and-drift hazard PA-239 fixed for
/// the admin shims. This invoker owns that logic once so the two clients stay thin wrappers over their own
/// public DTOs and endpoint paths and can never drift apart in transport/error handling.
///
/// The public generation DTOs and the <c>IStudioAppGenerationClient</c>/<c>IStudioMapGenerationClient</c>
/// interfaces are intentionally left unchanged: they are bound by name across the Studio pages, data
/// sources, and tests, so collapsing them into shared generics would be a broad, risky rename with no
/// behavior benefit. Only the duplicated plumbing is unified here.
/// </summary>
internal sealed class StudioGenerationHttpInvoker
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _subject;

    /// <param name="subject">The generation noun used in operator-facing error text, e.g. "app generation".</param>
    public StudioGenerationHttpInvoker(HttpClient httpClient, string? apiKey, string subject)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _subject = subject;
    }

    public async Task<StudioEndpointResult<TResponse>> SendAsync<TBody, TResponse>(
        HttpMethod method,
        string path,
        TBody? body,
        string contract,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server {_subject} endpoint could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server {_subject} endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var serverDetail = await TryReadErrorDetailAsync(response, cancellationToken).ConfigureAwait(false);
                return StudioEndpointResult<TResponse>.FromIssue(CreateIssue(contract, response.StatusCode, serverDetail));
            }

            // The generation endpoints return the BARE result object ({status, package, rationale, ...}) via
            // Results.Json — NOT wrapped in the StudioApiResponse {success, data} envelope the package
            // lifecycle endpoints use. Deserialize the payload directly; treating it as an envelope yields a
            // null Data and a false "no data" error.
            TResponse? payload;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                    "Unsupported",
                    contract,
                    $"The Honua server {_subject} response did not match the expected API shape: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (payload is not null)
            {
                return StudioEndpointResult<TResponse>.FromData(payload);
            }

            return StudioEndpointResult<TResponse>.FromIssue(new StudioEndpointIssue(
                "Unavailable",
                contract,
                $"The Honua server {_subject} response was empty.",
                (int)response.StatusCode));
        }
    }

    private static async Task<string?> TryReadErrorDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString()
                : root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
                    ? detailElement.GetString()
                    : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private StudioEndpointIssue CreateIssue(string contract, HttpStatusCode statusCode, string? serverDetail)
    {
        var state = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
            HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
            HttpStatusCode.BadRequest => "Validation failed",
            HttpStatusCode.Conflict => "Conflict",
            _ => "Unavailable"
        };

        var detail = serverDetail ?? statusCode switch
        {
            HttpStatusCode.Unauthorized => $"The Honua server rejected the {_subject} request because admin authentication is missing.",
            HttpStatusCode.Forbidden => $"The Honua server rejected the {_subject} request because the current principal lacks admin permission.",
            HttpStatusCode.NotFound => $"The Honua server does not expose the {_subject} contract.",
            HttpStatusCode.MethodNotAllowed => $"The Honua server exposes the route but not the required {_subject} verb.",
            HttpStatusCode.NotImplemented => $"The Honua server reports {_subject} is not implemented.",
            HttpStatusCode.BadRequest => $"The Honua server rejected the {_subject} request as invalid.",
            _ => string.Format(
                CultureInfo.InvariantCulture,
                "The Honua server returned HTTP {0} ({1}) for the {2} request.",
                (int)statusCode,
                statusCode,
                _subject)
        };

        return new StudioEndpointIssue(state, contract, detail, (int)statusCode);
    }
}
