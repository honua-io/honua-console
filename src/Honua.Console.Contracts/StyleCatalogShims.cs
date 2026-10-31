using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public sealed record HonuaOgcStylesClientOptions(Uri BaseUri, string? ApiKey = null, bool AuthorityBindingSupported = false);

/// <summary>Host-only request metadata; never serialized or taken from browser headers.</summary>
public interface IConsoleAuthorityBindingValidator
{
    Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default);
    Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

public static class ConsoleAuthorityRequestOptions
{
    /// <summary>Host-only canonical relative style route; never read from an HTTP header.</summary>
    public static readonly HttpRequestOptionsKey<string> RelativeStylePath = new("Honua.Console.Authority.RelativeStylePath");
    public static readonly HttpRequestOptionsKey<bool> Capture = new("Honua.Console.Authority.Capture");
    public static readonly HttpRequestOptionsKey<string> Expected = new("Honua.Console.Authority.Expected");
    public static readonly HttpRequestOptionsKey<string> Resolved = new("Honua.Console.Authority.Resolved");
}

/// <summary>
/// Stylesheet encoding the dual-mode style editor reads/writes over <c>/ogc/styles/{styleId}</c>. The canonical
/// store is MapLibre; GeoServices REST (<c>drawingInfo</c>) is a server-side projection (ADR-0002, ADR-0048).
/// </summary>
public enum HonuaOgcStyleEncoding
{
    /// <summary>Canonical MapLibre / Mapbox style JSON (<c>application/vnd.mapbox.style+json</c>).</summary>
    MapLibre,

    /// <summary>GeoServices REST drawingInfo renderer (the drawingInfo media type).</summary>
    Esri
}

/// <summary>A stylesheet body fetched in a given encoding.</summary>
public sealed record HonuaOgcStylesheet(string StyleId, HonuaOgcStyleEncoding Encoding, string Content)
{
    /// <summary>Strong version token returned with these exact canonical bytes.</summary>
    public string? EntityTag { get; init; }
    [JsonIgnore]
    public string? AuthorityBinding { get; init; }
}

/// <summary>
/// Outcome of a stylesheet write. On success the canonical MapLibre was stored; <see cref="UnsupportedSymbolizers"/>
/// carries non-blocking lossy-conversion warnings. On failure <see cref="Detail"/> explains the rejection
/// (e.g. a strict validation error).
/// </summary>
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

/// <summary>Authorized visible associations observed for one exact shared-style version.</summary>
public sealed record HonuaSharedStyleAssociation(string ResourceId, string Name, string? Title, string? ServiceId = null);
public sealed record HonuaSharedStyleConsumer(string Kind, string ItemId, string PackageKey, string ContentHash,
    string PublicationState, string? VersionId = null, string? DraftId = null, long? Generation = null, bool IsCurrent = false);
public sealed record HonuaSharedStyleImpact(string StyleId, long StyleVersion, string Etag,
    string ContentHash, string ImpactHash, IReadOnlyList<HonuaSharedStyleAssociation> Associations, bool CanEdit = false)
{
    public IReadOnlyList<HonuaSharedStyleConsumer> Consumers { get; init; } = [];
    public bool ImpactComplete { get; init; }
}

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

    /// <summary>Reads the server's stored or default style for a known published layer.</summary>
    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetLayerStylesheetAsync(
        int layerId, CancellationToken cancellationToken = default) => Task.FromResult(
            HonuaAdminEndpointResult<HonuaOgcStylesheet>.FromIssue(new HonuaAdminEndpointIssue(
                "Unsupported", "GET /api/styles/{layerId}.json", "Layer styles are not bound.")));

    /// <summary>Reads a style's stylesheet in the requested encoding (<c>GET /ogc/styles/{styleId}</c>).</summary>
    Task<HonuaAdminEndpointResult<HonuaOgcStylesheet>> GetStylesheetAsync(
        string styleId,
        HonuaOgcStyleEncoding encoding,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a style's stylesheet in the given encoding (<c>PUT /ogc/styles/{styleId}</c>). The server keeps
    /// MapLibre canonical and converts the GeoServices REST encoding. <paramref name="strict"/> sends
    /// <c>Prefer: handling=strict</c> so a lossy/invalid style is rejected rather than stored.
    /// </summary>
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

    /// <summary>Conditional shared-style update; unsupported clients never fall back to an unconditional write.</summary>
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
