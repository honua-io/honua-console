using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

// SHIM(honua-server ADR-0048, honua-server#1387): honua-server owns the OGC API - Styles surface. The
// styles list — GET /ogc/styles — advertises the stable styleIds the server can render, each with an
// optional human title and links to its stylesheet encodings/metadata, plus an optional server default
// style id. This is the canonical source of truth for "which styles exist", and is what the Studio map
// builder's per-layer style picker binds against so an author selects a real styleId instead of typing an
// opaque free-form string.
//
// Unlike the admin Console reads (/api/v1/console/...), the OGC API - Styles list is a PUBLIC OGC metadata
// document: it is NOT wrapped in the shared {success,data,message,timestamp} admin envelope, and it does
// not require the admin X-API-Key. This client therefore deserializes the OGC StylesList document directly
// and forwards the admin key only when one is configured (harmless on public reads, and future-proof if the
// surface is ever gated). When no server base URL is configured, the Console map builder keeps typing the
// legacy free-form style string rather than fabricating a style catalog.
//
// Swap these wire records for honua-sdk-dotnet projections when a consumable OGC API - Styles projection
// ships; until then this is the single Honua.Console.Contracts boundary for the styles list.

public sealed record HonuaOgcStylesClientOptions(Uri BaseUri, string? ApiKey = null);

/// <summary>One styleId the server advertises on <c>GET /ogc/styles</c> (ADR-0048).</summary>
public sealed record HonuaOgcStyleSummary(string Id, string? Title);

/// <summary>The OGC API - Styles styles list projection: the advertised styleIds plus the optional default.</summary>
public sealed record HonuaOgcStylesList(IReadOnlyList<HonuaOgcStyleSummary> Styles, string? Default);

/// <summary>
/// Typed client for the honua-server OGC API - Styles styles list (ADR-0048). The read targets the public
/// OGC metadata route <c>GET /ogc/styles</c> and returns the advertised styleIds, or a neutral capability
/// issue when the list cannot be read.
/// </summary>
public interface IHonuaOgcStylesClient
{
    Uri BaseUri { get; }

    /// <summary>Reads the server-advertised styleIds (the styles list), or a capability issue on failure.</summary>
    Task<HonuaAdminEndpointResult<HonuaOgcStylesList>> ListStylesAsync(CancellationToken cancellationToken = default);
}

public sealed class HonuaOgcStylesHttpClient : IHonuaOgcStylesClient, IDisposable
{
    private const string StylesPath = "/ogc/styles";
    private const string Contract = "GET /ogc/styles";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public HonuaOgcStylesHttpClient(HttpClient httpClient, HonuaOgcStylesClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public async Task<HonuaAdminEndpointResult<HonuaOgcStylesList>> ListStylesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, StylesPath);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
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
            return Unavailable(ex.Message);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var state = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
                    HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => "Unsupported",
                    _ => "Unavailable"
                };
                var detail = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "The Honua server rejected the OGC API - Styles request because authentication is missing.",
                    HttpStatusCode.Forbidden => "The current principal lacks permission to read the OGC API - Styles list.",
                    HttpStatusCode.NotFound => "The Honua server does not expose the OGC API - Styles list (GET /ogc/styles, ADR-0048).",
                    _ => $"The Honua server returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
                };
                return HonuaAdminEndpointResult<HonuaOgcStylesList>.FromIssue(
                    new HonuaAdminEndpointIssue(state, Contract, detail, (int)response.StatusCode));
            }

            OgcStylesListDocument? document;
            try
            {
                document = await response.Content
                    .ReadFromJsonAsync<OgcStylesListDocument>(JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                return HonuaAdminEndpointResult<HonuaOgcStylesList>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported",
                    Contract,
                    $"The Honua server OGC API - Styles response did not match the expected contract: {ex.Message}",
                    (int)response.StatusCode));
            }

            if (document?.Styles is null)
            {
                return HonuaAdminEndpointResult<HonuaOgcStylesList>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unavailable",
                    Contract,
                    "The Honua server OGC API - Styles response did not include a styles list.",
                    (int)response.StatusCode));
            }

            // Drop entries without a stable id (an id is required by the contract); a missing id makes the
            // entry unusable as a picker reference, so it is filtered rather than surfaced as a blank option.
            var styles = document.Styles
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => new HonuaOgcStyleSummary(entry.Id!, entry.Title))
                .ToArray();

            return HonuaAdminEndpointResult<HonuaOgcStylesList>.FromData(
                new HonuaOgcStylesList(styles, document.Default));
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static HonuaAdminEndpointResult<HonuaOgcStylesList> Unavailable(string detail) =>
        HonuaAdminEndpointResult<HonuaOgcStylesList>.FromIssue(new HonuaAdminEndpointIssue(
            "Unavailable",
            Contract,
            $"The Honua server OGC API - Styles endpoint could not be reached: {detail}"));

    // Wire shape of the OGC API - Styles styles list (mirrors honua-server StylesList/StyleEntry). The
    // PUBLIC OGC document is NOT wrapped in the admin {success,data} envelope, so it is deserialized directly.
    private sealed record OgcStylesListDocument
    {
        [JsonPropertyName("styles")]
        public IReadOnlyList<OgcStyleEntryDocument>? Styles { get; init; }

        [JsonPropertyName("default")]
        public string? Default { get; init; }
    }

    private sealed record OgcStyleEntryDocument
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }
    }
}
