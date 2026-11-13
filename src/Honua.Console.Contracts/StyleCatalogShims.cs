using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.Contracts;

public sealed record HonuaOgcStylesClientOptions(Uri BaseUri, string? ApiKey = null, bool AuthorityBindingSupported = false);

// Host-only request metadata. Never serialized and never taken from browser headers.
public interface IConsoleAuthorityBindingValidator
{
    Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default);
    Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

public interface IConsoleAuthorityChangeSource
{
    event Action? Changed;
}

public static class ConsoleAuthorityRequestOptions
{
    // Host-only canonical route. Never read from an HTTP header.
    public static readonly HttpRequestOptionsKey<string> RelativeStylePath = new("Honua.Console.Authority.RelativeStylePath");
    public static readonly HttpRequestOptionsKey<bool> Capture = new("Honua.Console.Authority.Capture");
    public static readonly HttpRequestOptionsKey<string> Expected = new("Honua.Console.Authority.Expected");
    public static readonly HttpRequestOptionsKey<string> Resolved = new("Honua.Console.Authority.Resolved");
}

// MapLibre is the canonical store. GeoServices drawingInfo is a server-side projection.
public enum HonuaOgcStyleEncoding
{
    MapLibre,

    Esri
}

public sealed record HonuaOgcStylesheet(string StyleId, HonuaOgcStyleEncoding Encoding, string Content)
{
    public string? EntityTag { get; init; }
    [JsonIgnore]
    public string? AuthorityBinding { get; init; }
}

// UnsupportedSymbolizers on a successful save are non-blocking lossy-conversion warnings.
public sealed record HonuaOgcStyleSaveResult(
    bool Succeeded,
    string? State,
    string? Detail,
    IReadOnlyList<string> UnsupportedSymbolizers)
{
    public static HonuaOgcStyleSaveResult Ok(IReadOnlyList<string> warnings) =>
        new(true, "Saved", null, warnings);

    public static HonuaOgcStyleSaveResult Fail(string state, string? detail) =>
        new(false, state, detail, Array.Empty<string>());
}

public sealed record HonuaSharedStyleAssociation(string ResourceId, string Name, string? Title, string? ServiceId = null);
public sealed record HonuaSharedStyleConsumer(string Kind, string ItemId, string PackageKey, string ContentHash,
    string PublicationState, string? VersionId = null, string? DraftId = null, long? Generation = null, bool IsCurrent = false);
public sealed record HonuaSharedStyleImpact(string StyleId, long StyleVersion, string Etag,
    string ContentHash, string ImpactHash, IReadOnlyList<HonuaSharedStyleAssociation> Associations, bool CanEdit = false)
{
    public IReadOnlyList<HonuaSharedStyleConsumer> Consumers { get; init; } = [];
    public bool ImpactComplete { get; init; }
}

public sealed record HonuaOgcStyleSummary(string Id, string? Title);

public sealed record HonuaOgcStylesList(IReadOnlyList<HonuaOgcStyleSummary> Styles, string? Default);

public interface IHonuaOgcStylesClient
{
    Uri BaseUri { get; }

    Task<HonuaAdminEndpointResult<HonuaOgcStylesList>> ListStylesAsync(CancellationToken cancellationToken = default);

    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(
        int layerId, CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported", "GET /api/styles/{layerId}.json", "Layer styles are not bound.")));

    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        CancellationToken cancellationToken = default);

    Task<HonuaOgcStyleSaveResult> UpdateStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        string content,
        bool strict,
        CancellationToken cancellationToken = default);
    Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported", "GET /ogc/styles/{id}/impact", "Shared-style impact review is not supported by this client.")));

    Task<HonuaOgcStyleSaveResult> UpdateStylesheetConditionallyAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash,
        CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaOgcStyleSaveResult.Fail("Unsupported", "Versioned shared-style updates are not supported by this client."));

    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}", "Operator-bound style reading is unavailable.")));

    Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}/impact", "Operator-bound style editing is unavailable.")));
    Task<HonuaOgcStyleSaveResult> UpdateStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) =>
        Task.FromResult(HonuaOgcStyleSaveResult.Fail("Unsupported", "Operator-bound style editing is unavailable."));
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
    private readonly bool _authorityBindingSupported;

    public HonuaOgcStylesHttpClient(HttpClient httpClient, HonuaOgcStylesClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        BaseUri = options.BaseUri;
        _apiKey = options.ApiKey;
        _authorityBindingSupported = options.AuthorityBindingSupported;
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= BaseUri;
    }

    public Uri BaseUri { get; }

    public async Task<HonuaAdminEndpointResult<HonuaOgcStylesList>> ListStylesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, StylesPath);
        if (_authorityBindingSupported) request.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, StylesPath.TrimStart('/'));
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

    private const string MapboxMediaType = "application/vnd.mapbox.style+json";
    private const string EsriMediaType = "application/vnd.esri.drawinginfo+json";

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(
        int layerId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(layerId);
        return ReadStylesheetAsync(layerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HonuaOgcStyleEncoding.MapLibre, $"/api/styles/{layerId}.json", "GET /api/styles/{layerId}.json", cancellationToken);
    }

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId, HonuaOgcStyleEncoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);
        return ReadStylesheetAsync(styleId, encoding, $"{StylesPath}/{Uri.EscapeDataString(styleId)}",
            "GET /ogc/styles/{styleId}", cancellationToken, captureAuthority: true);
    }

    public Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string authorityBinding, CancellationToken cancellationToken = default) =>
        _authorityBindingSupported && !string.IsNullOrWhiteSpace(authorityBinding)
            ? ReadStylesheetAsync(styleId, encoding, $"{StylesPath}/{Uri.EscapeDataString(styleId)}", "GET /ogc/styles/{styleId}", cancellationToken, authorityBinding: authorityBinding)
            : Task.FromResult(HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}", "Operator-bound style reading is unavailable.")));

    private async Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> ReadStylesheetAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string path, string contract, CancellationToken cancellationToken, bool captureAuthority = false, string? authorityBinding = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Accept", MediaTypeFor(encoding));
        if (authorityBinding is not null)
        {
            request.Options.Set(ConsoleAuthorityRequestOptions.Expected, authorityBinding);
            request.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, path.TrimStart('/'));
        }
        else if (_authorityBindingSupported && captureAuthority)
        {
            request.Options.Set(ConsoleAuthorityRequestOptions.Capture, true);
            request.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, path.TrimStart('/'));
        }
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", contract, $"The Honua server OGC API - Styles endpoint could not be reached: {ex.Message}"));
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var state = response.StatusCode switch
                {
                    HttpStatusCode.NotFound or HttpStatusCode.NotAcceptable => "Unsupported",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
                    _ => "Unavailable"
                };
                return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new HonuaAdminEndpointIssue(
                    state, contract,
                    $"The Honua server returned HTTP {(int)response.StatusCode} reading the stylesheet.",
                    (int)response.StatusCode));
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromData(
                new HonuaOgcStylesheet(styleId, encoding, content) { EntityTag = response.Headers.ETag?.ToString(), AuthorityBinding = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Resolved, out var binding) ? binding : null });
        }
    }

    public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactAsync(string styleId, CancellationToken cancellationToken = default) => GetSharedStyleImpactCoreAsync(styleId, null, cancellationToken);
    public Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactBoundAsync(string styleId, string authorityBinding, CancellationToken cancellationToken = default) =>
        _authorityBindingSupported && !string.IsNullOrWhiteSpace(authorityBinding) ? GetSharedStyleImpactCoreAsync(styleId, authorityBinding, cancellationToken)
        : Task.FromResult(HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new("Unsupported", "GET /ogc/styles/{id}/impact", "Operator-bound style editing is unavailable.")));
    private async Task<HonuaAdminEndpointResult<HonuaSharedStyleImpact>> GetSharedStyleImpactCoreAsync(
        string styleId, string? authorityBinding, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);
        var path = $"{StylesPath}/{Uri.EscapeDataString(styleId)}/impact";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (authorityBinding is not null)
        {
            request.Options.Set(ConsoleAuthorityRequestOptions.Expected, authorityBinding);
            request.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, path.TrimStart('/'));
        }
        if (!string.IsNullOrWhiteSpace(_apiKey)) request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var state = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? "Missing permission"
                    : response.StatusCode == HttpStatusCode.NotFound ? "Unsupported" : "Unavailable";
                return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new HonuaAdminEndpointIssue(
                    state, path, $"The shared-style impact could not be read (HTTP {(int)response.StatusCode}).", (int)response.StatusCode));
            }
            var impact = await JsonSerializer.DeserializeAsync<HonuaSharedStyleImpact>(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), JsonOptions, cancellationToken).ConfigureAwait(false);
            if (impact is null || impact.StyleId != styleId || impact.StyleVersion < 1 || impact.Associations is null || impact.Consumers is null
                || string.IsNullOrWhiteSpace(impact.Etag) || response.Headers.ETag?.ToString() != impact.Etag || !IsHash(impact.ContentHash) || !IsHash(impact.ImpactHash))
                return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new HonuaAdminEndpointIssue(
                    "Unsupported", path, "The shared-style impact response did not identify a versioned review."));
            return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromData(impact);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return HonuaAdminEndpointResult<HonuaSharedStyleImpact>.FromIssue(new HonuaAdminEndpointIssue(
                "Unavailable", path, "The shared-style impact request did not complete."));
        }
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    public Task<HonuaOgcStyleSaveResult> UpdateStylesheetAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict,
        CancellationToken cancellationToken = default) =>
        UpdateStylesheetCoreAsync(styleId, encoding, content, strict, null, null, cancellationToken);

    public Task<HonuaOgcStyleSaveResult> UpdateStylesheetConditionallyAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash,
        CancellationToken cancellationToken = default) => UpdateStylesheetConditionallyCoreAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, null, cancellationToken);
    public Task<HonuaOgcStyleSaveResult> UpdateStylesheetBoundAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string authorityBinding, CancellationToken cancellationToken = default) =>
        _authorityBindingSupported && !string.IsNullOrWhiteSpace(authorityBinding) ? UpdateStylesheetConditionallyCoreAsync(styleId, encoding, content, strict, expectedEntityTag, expectedImpactHash, authorityBinding, cancellationToken)
        : Task.FromResult(HonuaOgcStyleSaveResult.Fail("Unsupported", "Operator-bound style editing is unavailable."));
    private Task<HonuaOgcStyleSaveResult> UpdateStylesheetConditionallyCoreAsync(string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, string expectedEntityTag, string expectedImpactHash, string? authorityBinding, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedEntityTag) || expectedEntityTag.Length > 128 || !EntityTagHeaderValue.TryParse(expectedEntityTag, out var version)
            || version.IsWeak || version.Tag == "*")
            return Task.FromResult(HonuaOgcStyleSaveResult.Fail("Version required", "Reload the shared style to obtain its current version before saving."));
        if (expectedImpactHash is null || expectedImpactHash.Length != 64 || expectedImpactHash.Any(character => !char.IsAsciiHexDigitLower(character)))
            return Task.FromResult(HonuaOgcStyleSaveResult.Fail("Impact review required", "Review the current shared-style associations before saving."));
        return UpdateStylesheetCoreAsync(styleId, encoding, content, strict, version, expectedImpactHash, cancellationToken, authorityBinding);
    }

    private async Task<HonuaOgcStyleSaveResult> UpdateStylesheetCoreAsync(
        string styleId, HonuaOgcStyleEncoding encoding, string content, bool strict, EntityTagHeaderValue? expectedVersion, string? expectedImpactHash,
        CancellationToken cancellationToken, string? authorityBinding = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(styleId);
        ArgumentNullException.ThrowIfNull(content);

        var path = $"{StylesPath}/{Uri.EscapeDataString(styleId)}";
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(content, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeFor(encoding));
        if (authorityBinding is not null)
        {
            request.Options.Set(ConsoleAuthorityRequestOptions.Expected, authorityBinding);
            request.Options.Set(ConsoleAuthorityRequestOptions.RelativeStylePath, path.TrimStart('/'));
        }
        if (expectedVersion is not null) request.Headers.IfMatch.Add(expectedVersion);
        if (expectedImpactHash is not null) request.Headers.Add("X-Honua-Style-Impact", expectedImpactHash);
        // handling=strict tells the server to reject a lossy style instead of storing it.
        if (strict)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "handling=strict");
        }

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HonuaOgcStyleSaveResult.Fail("Unavailable", $"The Honua server could not be reached: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
            {
                return HonuaOgcStyleSaveResult.Ok(ReadUnsupportedSymbolizers(response));
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var state = response.StatusCode switch
            {
                HttpStatusCode.PreconditionFailed => "Conflict",
                HttpStatusCode.BadRequest => "Rejected",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Missing permission",
                HttpStatusCode.NotFound or HttpStatusCode.UnsupportedMediaType => "Unsupported",
                _ => "Unavailable"
            };
            return HonuaOgcStyleSaveResult.Fail(state, response.StatusCode == HttpStatusCode.PreconditionFailed
                ? "The shared style changed. Your edits are retained. Review the current version before saving again."
                : ParseProblemDetail(body) ?? $"HTTP {(int)response.StatusCode}");
        }
    }

    private static string MediaTypeFor(HonuaOgcStyleEncoding encoding) =>
        encoding == HonuaOgcStyleEncoding.Esri ? EsriMediaType : MapboxMediaType;

    private static IReadOnlyList<string> ReadUnsupportedSymbolizers(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Style-Unsupported-Symbolizers", out var values))
        {
            return values
                .SelectMany(v => v.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static string? ParseProblemDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString();
                }

                if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    return title.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to the raw body.
        }

        return body.Length > 400 ? body[..400] : body;
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
