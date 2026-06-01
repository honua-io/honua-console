using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Independent read-only verification oracle for the live-server operation→output integration suite
/// (console-integration-test-plan.md §2.2, Wave 1). It talks DIRECTLY to honua-server's canonical read
/// APIs — the admin layer registry, the GeoServices FeatureServer metadata + query surface, the admin
/// service-settings surface, the console catalog, and STAC — rather than through the console's own data
/// sources. This is the single most important rule of the suite (plan rule #2): assert server-side state
/// through a DIFFERENT API than the one the operation went through, so the test proves the server is
/// actually configured correctly rather than re-reading the same code path.
///
/// The verifier owns no mutation. It reuses the Testcontainer base URL + admin key, accepts the
/// dev/self-signed certificate for TLS fixtures, and parses responses defensively from raw JSON so it
/// stays resilient to incidental shape changes in the read surfaces.
/// </summary>
public sealed class ServerStateVerifier : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string? _adminApiKey;

    public ServerStateVerifier(Uri baseAddress, string? adminApiKey)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);

        var handler = new HttpClientHandler();
        if (string.Equals(baseAddress.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _http = new HttpClient(handler) { BaseAddress = baseAddress };
        _adminApiKey = adminApiKey;
    }

    public void Dispose() => _http.Dispose();

    // ---------------------------------------------------------------------------------------------
    //  Admin layer registry: GET /api/v1/admin/connections/{id}/layers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the published-layer registry for a connection and returns the layer matching <paramref name="layerId"/>,
    /// or <c>null</c> when absent. Verifies the layer was actually registered (enabled flag, name, source table).
    /// </summary>
    public async Task<VerifiedLayerRegistration?> GetRegisteredLayerAsync(
        string connectionId,
        string serviceName,
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers?serviceName={Uri.EscapeDataString(serviceName)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var layer in data.EnumerateArray())
        {
            if (GetInt(layer, "layerId") == layerId)
            {
                return new VerifiedLayerRegistration(
                    layerId,
                    GetString(layer, "layerName"),
                    GetString(layer, "schema"),
                    GetString(layer, "table"),
                    GetString(layer, "serviceName"),
                    GetString(layer, "geometryType"),
                    GetInt(layer, "srid"),
                    GetBool(layer, "enabled"));
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------------------------
    //  FeatureServer metadata: GET /rest/services/{service}/FeatureServer/{layerId}
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the GeoServices FeatureServer layer metadata: fields (name → esri type), geometryType,
    /// the extent + its spatial-reference wkid, and the capability list. This is the canonical
    /// service-metadata read, independent of the admin publish path.
    /// </summary>
    public async Task<VerifiedFeatureServerLayer?> GetFeatureServerLayerAsync(
        string serviceName,
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/rest/services/{Uri.EscapeDataString(serviceName)}/FeatureServer/{layerId.ToString(CultureInfo.InvariantCulture)}?f=json";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        // A FeatureServer error payload carries an "error" object instead of layer metadata.
        if (root.TryGetProperty("error", out _) || !root.TryGetProperty("geometryType", out _) && !root.TryGetProperty("fields", out _))
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("fields", out var fieldsElement) && fieldsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fieldsElement.EnumerateArray())
            {
                var name = GetString(field, "name");
                var type = GetString(field, "type");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    fields[name!] = type ?? string.Empty;
                }
            }
        }

        var capabilities = (GetString(root, "capabilities") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        VerifiedExtent? extent = null;
        if (root.TryGetProperty("extent", out var extentElement) && extentElement.ValueKind == JsonValueKind.Object)
        {
            int? wkid = null;
            if (extentElement.TryGetProperty("spatialReference", out var spatialRef) && spatialRef.ValueKind == JsonValueKind.Object)
            {
                wkid = GetInt(spatialRef, "wkid") ?? GetInt(spatialRef, "latestWkid");
            }

            extent = new VerifiedExtent(
                GetDouble(extentElement, "xmin"),
                GetDouble(extentElement, "ymin"),
                GetDouble(extentElement, "xmax"),
                GetDouble(extentElement, "ymax"),
                wkid);
        }

        return new VerifiedFeatureServerLayer(
            GetString(root, "name"),
            GetString(root, "geometryType"),
            fields,
            capabilities,
            extent);
    }

    // ---------------------------------------------------------------------------------------------
    //  FeatureServer query: GET /rest/services/{service}/FeatureServer/{layerId}/query
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a FeatureServer query and projects the returned feature count, attribute maps, and the
    /// returned spatial-reference wkid. Proves the layer is actually queryable with the right data + SRID,
    /// and (via the <paramref name="where"/> filter) that the configured fields are real and filterable.
    /// </summary>
    public async Task<VerifiedQueryResult?> QueryFeatureServerAsync(
        string serviceName,
        int layerId,
        string where,
        string outFields = "*",
        CancellationToken cancellationToken = default)
    {
        var path = $"/rest/services/{Uri.EscapeDataString(serviceName)}/FeatureServer/{layerId.ToString(CultureInfo.InvariantCulture)}/query"
            + $"?f=json&where={Uri.EscapeDataString(where)}&outFields={Uri.EscapeDataString(outFields)}&returnGeometry=false";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (root.TryGetProperty("error", out _) || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var rows = new List<IReadOnlyDictionary<string, JsonElement>>();
        foreach (var feature in features.EnumerateArray())
        {
            if (feature.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in attributes.EnumerateObject())
                {
                    map[property.Name] = property.Value.Clone();
                }

                rows.Add(map);
            }
        }

        int? wkid = null;
        if (root.TryGetProperty("spatialReference", out var spatialRef) && spatialRef.ValueKind == JsonValueKind.Object)
        {
            wkid = GetInt(spatialRef, "wkid") ?? GetInt(spatialRef, "latestWkid");
        }

        return new VerifiedQueryResult(rows, wkid);
    }

    // ---------------------------------------------------------------------------------------------
    //  Admin service settings: GET /api/v1/admin/services/{service}/settings
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the admin service-settings projection (enabled protocols, available protocols) for a service.
    /// Returns <c>null</c> when the server does not expose settings for the service.
    /// </summary>
    public async Task<VerifiedServiceSettings?> GetServiceSettingsAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/settings";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new VerifiedServiceSettings(
            GetString(data, "serviceName"),
            ReadStringArray(data, "enabledProtocols"),
            ReadStringArray(data, "availableProtocols"));
    }

    // ---------------------------------------------------------------------------------------------
    //  Console catalog: GET /api/v1/console/content/search?query=...
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the console catalog search listing and returns the titles of the returned items. Used to
    /// independently confirm published content landed in the catalog (best-effort; returns an empty list
    /// when the surface is unavailable).
    /// </summary>
    public async Task<IReadOnlyList<string>> SearchCatalogTitlesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/content/search?query={Uri.EscapeDataString(query)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        // The console content search returns either a bare array or an items[] envelope; handle both.
        var root = document.RootElement;
        JsonElement items;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (root.TryGetProperty("items", out var envelopeItems) && envelopeItems.ValueKind == JsonValueKind.Array)
        {
            items = envelopeItems;
        }
        else if (root.TryGetProperty("data", out var dataItems) && dataItems.ValueKind == JsonValueKind.Array)
        {
            items = dataItems;
        }
        else
        {
            return [];
        }

        var titles = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            var title = GetString(item, "title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                titles.Add(title!);
            }
        }

        return titles;
    }

    // ---------------------------------------------------------------------------------------------
    //  STAC: GET /stac/collections
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the STAC collection listing and returns the collection ids. Used for the open-data / catalog
    /// read family; best-effort (returns an empty list when STAC is unavailable).
    /// </summary>
    public async Task<IReadOnlyList<string>> ListStacCollectionIdsAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("/stac/collections", cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("collections", out var collections) || collections.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new List<string>();
        foreach (var collection in collections.EnumerateArray())
        {
            var id = GetString(collection, "id");
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id!);
            }
        }

        return ids;
    }

    // ---------------------------------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------------------------------

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static double? GetDouble(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static bool? GetBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
            {
                values.Add(value);
            }
        }

        return values;
    }
}

public sealed record VerifiedLayerRegistration(
    int LayerId,
    string? LayerName,
    string? Schema,
    string? Table,
    string? ServiceName,
    string? GeometryType,
    int? Srid,
    bool? Enabled);

public sealed record VerifiedFeatureServerLayer(
    string? Name,
    string? GeometryType,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Capabilities,
    VerifiedExtent? Extent);

public sealed record VerifiedExtent(double? XMin, double? YMin, double? XMax, double? YMax, int? Wkid);

public sealed record VerifiedQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Features,
    int? SpatialReferenceWkid)
{
    public int Count => Features.Count;
}

public sealed record VerifiedServiceSettings(
    string? ServiceName,
    IReadOnlyList<string> EnabledProtocols,
    IReadOnlyList<string> AvailableProtocols);
