using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Honua.Console.IntegrationTests;

// The verifier calls a different API so it does not re-read the operation's own projection.
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

    public async Task<VerifiedLayerRegistration?> GetRegisteredLayerAsync(
        string connectionId,
        string serviceName,
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/connections/{Uri.EscapeDataString(connectionId)}/layers?serviceName={Uri.EscapeDataString(serviceName)}";
        for (var pageNumber = 1; ; pageNumber++)
        {
            using var document = await GetJsonAsync($"{path}&page={pageNumber}", cancellationToken).ConfigureAwait(false);
            if (document is null) return null;
            var data = document.RootElement.GetProperty("data");
            var items = data.GetProperty("items");
            var pageSize = data.GetProperty("pageSize").GetInt32();
            var total = data.GetProperty("total").GetInt32();
            if (data.GetProperty("page").GetInt32() != pageNumber || pageSize <= 0 || total < 0
                || items.GetArrayLength() > pageSize || (items.GetArrayLength() == 0 && total > 0))
                throw new InvalidDataException("The admin layer registry returned an invalid page.");
            var matches = items.EnumerateArray().Where(layer => GetInt(layer, "layerId") == layerId).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("The admin layer registry returned duplicate layer identities.");
            if (matches.Length == 1) return Registration(matches[0], layerId);
            if ((long)pageNumber * pageSize >= total) return null;
        }
    }

    private static VerifiedLayerRegistration Registration(JsonElement layer, int layerId) => new(
        layerId,
        GetString(layer, "layerName"),
        GetString(layer, "schema"),
        GetString(layer, "table"),
        GetString(layer, "serviceName"),
        GetString(layer, "geometryType"),
        GetInt(layer, "srid"),
        GetBool(layer, "enabled"));

    // ---------------------------------------------------------------------------------------------
    //  FeatureServer metadata: GET /rest/services/{service}/FeatureServer/{layerId}
    // ---------------------------------------------------------------------------------------------

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

        VerifiedAccessPolicy? accessPolicy = null;
        if (data.TryGetProperty("accessPolicy", out var policy) && policy.ValueKind == JsonValueKind.Object)
        {
            accessPolicy = new VerifiedAccessPolicy(
                GetBool(policy, "allowAnonymous"),
                GetBool(policy, "allowAnonymousWrite"),
                ReadStringArray(policy, "allowedRoles"),
                ReadStringArray(policy, "allowedWriteRoles"));
        }

        return new VerifiedServiceSettings(
            GetString(data, "serviceName"),
            ReadStringArray(data, "enabledProtocols"),
            ReadStringArray(data, "availableProtocols"),
            accessPolicy);
    }

    // ---------------------------------------------------------------------------------------------
    //  GeoServices service-root reachability: GET /rest/services/{service}/{protocol}
    // ---------------------------------------------------------------------------------------------

    // Protocol reachability is probed on the protocol surface, not only the admin settings projection.
    public async Task<bool> GeoServicesProtocolResolvesAsync(
        string serviceName,
        string protocol,
        CancellationToken cancellationToken = default)
    {
        var path = $"/rest/services/{Uri.EscapeDataString(serviceName)}/{Uri.EscapeDataString(protocol)}?f=json";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return false;
        }

        var root = document.RootElement;
        // A GeoServices error document carries an "error" object; a real service document carries the
        // protocol's metadata (currentVersion / layers / capabilities).
        return root.ValueKind == JsonValueKind.Object
            && !root.TryGetProperty("error", out _)
            && (root.TryGetProperty("currentVersion", out _)
                || root.TryGetProperty("layers", out _)
                || root.TryGetProperty("capabilities", out _)
                || root.TryGetProperty("serviceDescription", out _));
    }

    // ---------------------------------------------------------------------------------------------
    //  Console catalog: GET /api/v1/console/content/search?query=...
    // ---------------------------------------------------------------------------------------------

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
    //  Console catalog detail: GET /api/v1/console/content/{id}
    // ---------------------------------------------------------------------------------------------

    public async Task<VerifiedCatalogItem?> GetCatalogItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/content/{Uri.EscapeDataString(itemId)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        // The console content GET returns either the bare item or a {success,data,...} envelope.
        var root = document.RootElement;
        var item = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        var id = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return new VerifiedCatalogItem(
            id!,
            GetString(item, "title"),
            GetString(item, "itemType"),
            GetString(item, "visibility"));
    }

    // ---------------------------------------------------------------------------------------------
    //  Content publication admin detail: GET /api/v1/console/publications/{id}
    // ---------------------------------------------------------------------------------------------

    public async Task<VerifiedPublicationRoute?> GetPublicationRouteAsync(
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/publications/{Uri.EscapeDataString(publicationId)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("route", out var route) || route.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? visibility = null;
        if (route.TryGetProperty("policy", out var policy) && policy.ValueKind == JsonValueKind.Object)
        {
            visibility = GetString(policy, "visibility");
        }

        return new VerifiedPublicationRoute(
            GetString(route, "publicationId"),
            GetString(route, "routeSlug"),
            GetString(route, "routePath"),
            GetString(route, "kind"),
            GetInt(route, "activeRevision"),
            visibility);
    }

    // ---------------------------------------------------------------------------------------------
    //  Published-route reachability (anonymous): GET /api/v1/published/{routeSlug}
    // ---------------------------------------------------------------------------------------------

    // Publication reachability is checked with no admin key so visibility is not proven with a privileged client.
    public Task<VerifiedAnonymousFetch> FetchPublishedRouteAnonymouslyAsync(
        string routeSlug,
        CancellationToken cancellationToken = default)
    {
        var trimmed = routeSlug.TrimStart('/');
        return FetchAnonymousAsync($"/api/v1/published/{trimmed}", cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    //  Anonymous Console Share resolution: GET /api/v1/console/share/...
    // ---------------------------------------------------------------------------------------------

    // An unknown, expired, revoked, or forbidden anonymous token returns 404 with a constant denial body.
    public Task<VerifiedAnonymousFetch> ResolvePublicLinkAnonymouslyAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/link/{Uri.EscapeDataString(token)}", cancellationToken);

    // A private, missing, or no-longer-public item returns 404.
    public Task<VerifiedAnonymousFetch> ResolvePublicContentAnonymouslyAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/content/{Uri.EscapeDataString(itemId)}", cancellationToken);

    // An expired or disabled embed token returns 404.
    public Task<VerifiedAnonymousFetch> RedeemEmbedTokenAnonymouslyAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/embed/{Uri.EscapeDataString(token)}/redeem", cancellationToken, HttpMethod.Post);

    // ---------------------------------------------------------------------------------------------
    //  Temporal capability: GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/capabilities
    // ---------------------------------------------------------------------------------------------

    // A descriptor with supportsHistory false means the route is mounted but unsupported; null also covers transport failure, an empty body, invalid JSON, or a missing supportsHistory field, so null does not prove the route is absent.
    public async Task<VerifiedTemporalCapability?> GetTemporalCapabilityAsync(
        string serviceId,
        int layerId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{layerId.ToString(CultureInfo.InvariantCulture)}/capabilities";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("supportsHistory", out _))
        {
            return null;
        }

        return new VerifiedTemporalCapability(
            GetString(root, "serviceId"),
            GetInt(root, "layerId"),
            GetBool(root, "supportsHistory") ?? false,
            GetBool(root, "supportsAsOf") ?? false,
            GetString(root, "temporalColumn"),
            GetString(root, "cursorKind"),
            GetLong(root, "currentGeneration"));
    }

    // An absent route and an unsupported as-of response both become null here, so this result cannot tell them apart.
    public async Task<VerifiedTemporalAsOf?> ReadTemporalAsOfAsync(
        string serviceId,
        int layerId,
        long? generation = null,
        CancellationToken cancellationToken = default)
    {
        var query = generation is { } gen ? $"?generation={gen.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        var path = $"/api/v1/temporal/services/{Uri.EscapeDataString(serviceId)}/layers/{layerId.ToString(CultureInfo.InvariantCulture)}/as-of{query}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var changes = new List<VerifiedTemporalChange>();
        foreach (var feature in features.EnumerateArray())
        {
            changes.Add(new VerifiedTemporalChange(
                GetLong(feature, "objectId"),
                GetString(feature, "operation")));
        }

        return new VerifiedTemporalAsOf(
            GetLong(root, "resolvedGeneration"),
            GetLong(root, "currentGeneration"),
            GetString(root, "requestedCursorKind"),
            changes);
    }

    // ---------------------------------------------------------------------------------------------
    //  Replica management: GET /api/v1/admin/services/{serviceId}/replicas (+ /{replicaId})
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<VerifiedReplica>> ListReplicasAsync(
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!data.TryGetProperty("replicas", out var replicas) || replicas.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<VerifiedReplica>();
        foreach (var replica in replicas.EnumerateArray())
        {
            results.Add(ToVerifiedReplica(replica));
        }

        return results;
    }

    public async Task<VerifiedReplica?> GetReplicaAsync(
        string serviceId,
        string replicaId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/services/{Uri.EscapeDataString(serviceId)}/replicas/{Uri.EscapeDataString(replicaId)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ToVerifiedReplica(data);
    }

    private static VerifiedReplica ToVerifiedReplica(JsonElement replica) =>
        new(
            GetString(replica, "replicaId"),
            GetString(replica, "replicaName"),
            GetString(replica, "serviceId"),
            GetString(replica, "syncModel"),
            GetLong(replica, "lastSyncGeneration"));

    // ---------------------------------------------------------------------------------------------
    //  Catalog discovery registry: GET /api/v1/console/catalog-endpoints/{workspaceId}
    // ---------------------------------------------------------------------------------------------

    public async Task<VerifiedCatalogRegistry?> GetCatalogDiscoveryRegistryAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/catalog-endpoints/{Uri.EscapeDataString(workspaceId)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var endpoints = new List<VerifiedCatalogEndpoint>();
        if (data.TryGetProperty("endpoints", out var endpointsElement) && endpointsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var endpoint in endpointsElement.EnumerateArray())
            {
                endpoints.Add(new VerifiedCatalogEndpoint(
                    GetString(endpoint, "key"),
                    GetString(endpoint, "dialect"),
                    GetBool(endpoint, "enabled"),
                    GetInt(endpoint, "entries")));
            }
        }

        return new VerifiedCatalogRegistry(GetString(data, "workspaceId"), endpoints);
    }

    // A mounted gated route returns 401, an absent route returns 404, and status zero means the server was unreachable.
    public Task<int> ProbeAdminRouteAnonymousStatusAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ProbeStatusAsync(path, cancellationToken);

    // Mutation probes send a JSON body so model binding does not return 400 before the auth check, and 404 is not a denial.
    public async Task<int> ProbeAnonymousMutationStatusAsync(
        HttpMethod method,
        string path,
        string? jsonBody = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return (int)response.StatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 0;
        }
    }

    // ---------------------------------------------------------------------------------------------
    //  Version / capabilities (contract pinning): GET /api/v1/admin/version + /capabilities
    // ---------------------------------------------------------------------------------------------

    private static JsonElement UnwrapData(JsonElement root) =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : root;

    public async Task<VerifiedServerContract?> GetServerContractAsync(CancellationToken cancellationToken = default)
    {
        string? version = null;
        string? metadataApiVersion = null;
        string? metadataSchemaVersion = null;
        var versionRouteMounted = false;
        var capabilitiesRouteMounted = false;

        using (var versionDoc = await GetJsonAsync("/api/v1/admin/version", cancellationToken).ConfigureAwait(false))
        {
            if (versionDoc is not null && versionDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = UnwrapData(versionDoc.RootElement);
                versionRouteMounted = true;
                version = GetString(root, "version");
                metadataApiVersion = GetString(root, "metadataApiVersion");
                metadataSchemaVersion = GetString(root, "metadataSchemaVersion");
            }
        }

        using (var capabilitiesDoc = await GetJsonAsync("/api/v1/admin/capabilities", cancellationToken).ConfigureAwait(false))
        {
            if (capabilitiesDoc is not null && capabilitiesDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = UnwrapData(capabilitiesDoc.RootElement);
                capabilitiesRouteMounted = true;
                version ??= GetString(root, "serverVersion");
                metadataApiVersion ??= GetString(root, "metadataApiVersion");
                metadataSchemaVersion ??= GetString(root, "metadataSchemaVersion");
            }
        }

        if (!versionRouteMounted && !capabilitiesRouteMounted)
        {
            return null;
        }

        return new VerifiedServerContract(
            version,
            metadataApiVersion,
            metadataSchemaVersion,
            versionRouteMounted,
            capabilitiesRouteMounted);
    }

    // ---------------------------------------------------------------------------------------------
    //  Collaboration comments: GET /api/v1/console/maps/{mapId}/collab/comments (+ /activity)
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<VerifiedCommentThread>> ListCollabThreadsAsync(
        string mapId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/maps/{Uri.EscapeDataString(mapId)}/collab/comments";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!data.TryGetProperty("threads", out var threads) || threads.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<VerifiedCommentThread>();
        foreach (var thread in threads.EnumerateArray())
        {
            var bodies = new List<string>();
            if (thread.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var body = GetString(message, "body");
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        bodies.Add(body!);
                    }
                }
            }

            results.Add(new VerifiedCommentThread(
                GetString(thread, "threadId"),
                GetString(thread, "featureLabel"),
                GetString(thread, "layerRef"),
                GetBool(thread, "resolved") ?? false,
                GetInt(thread, "commentCount"),
                bodies));
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> ListCollabActivityAsync(
        string mapId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/console/maps/{Uri.EscapeDataString(mapId)}/collab/activity";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null || !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!data.TryGetProperty("activity", out var activity) || activity.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var actions = new List<string>();
        foreach (var entry in activity.EnumerateArray())
        {
            var action = GetString(entry, "action");
            if (!string.IsNullOrWhiteSpace(action))
            {
                actions.Add(action!);
            }
        }

        return actions;
    }

    // ---------------------------------------------------------------------------------------------
    //  Studio content-item version: GET /api/v1/studio/content-items/{itemId}/versions[/{versionId}]
    // ---------------------------------------------------------------------------------------------

    public async Task<VerifiedStudioContentVersion?> GetStudioContentVersionAsync(
        Guid itemId,
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/studio/content-items/{itemId}/versions/{versionId}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        // The Studio API wraps the version in the shared {success,data,...} envelope; the version may also
        // be returned bare. Handle both.
        var root = document.RootElement;
        var item = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("versionId", out _))
        {
            return null;
        }

        string? family = null;
        string? schemaVersion = null;
        string? visibility = null;
        string? route = null;
        if (item.TryGetProperty("envelope", out var envelope) && envelope.ValueKind == JsonValueKind.Object)
        {
            family = GetString(envelope, "family");
            schemaVersion = GetString(envelope, "schemaVersion");
            if (envelope.TryGetProperty("publicationIntent", out var intent) && intent.ValueKind == JsonValueKind.Object)
            {
                visibility = GetString(intent, "visibility");
                route = GetString(intent, "route");
            }
        }

        return new VerifiedStudioContentVersion(
            GetString(item, "itemId"),
            GetString(item, "versionId"),
            GetInt(item, "versionNumber"),
            GetString(item, "packageKey"),
            family,
            schemaVersion,
            GetString(item, "contentHash"),
            visibility,
            route);
    }

    public async Task<IReadOnlyList<int>> ListStudioContentVersionNumbersAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/studio/content-items/{itemId}/versions";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return [];
        }

        var root = document.RootElement;
        var body = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var numbers = new List<int>();
        foreach (var version in versions.EnumerateArray())
        {
            if (GetInt(version, "versionNumber") is { } number)
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    // ---------------------------------------------------------------------------------------------
    //  Form package: GET /api/v1/admin/forms/packages/{formId}[/versions/{version}]
    //               + GET /api/v1/forms/packages/{formId}/offline-policy
    // ---------------------------------------------------------------------------------------------

    public async Task<VerifiedFormPackage?> GetFormPackageAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/admin/forms/packages/{Uri.EscapeDataString(formId)}";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var item = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("formId", out _))
        {
            return null;
        }

        string? title = null;
        string? serviceId = null;
        int? layerId = null;
        if (item.TryGetProperty("package", out var package) && package.ValueKind == JsonValueKind.Object)
        {
            title = GetString(package, "title");
            if (package.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.Object)
            {
                serviceId = GetString(target, "serviceId");
                layerId = GetInt(target, "layerId");
            }
        }

        return new VerifiedFormPackage(
            GetString(item, "formId"),
            title,
            serviceId,
            layerId,
            GetInt(item, "version"),
            GetString(item, "status"));
    }

    public async Task<VerifiedFormOfflinePolicy?> GetFormOfflinePolicyAsync(
        string formId,
        CancellationToken cancellationToken = default)
    {
        var path = $"/api/v1/forms/packages/{Uri.EscapeDataString(formId)}/offline-policy";
        using var document = await GetJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        var item = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            ? data
            : root;

        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("enabled", out _))
        {
            return null;
        }

        var transports = new List<string>();
        if (item.TryGetProperty("availableTransports", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var transport in array.EnumerateArray())
            {
                if (transport.ValueKind == JsonValueKind.String && transport.GetString() is { Length: > 0 } value)
                {
                    transports.Add(value);
                }
            }
        }

        return new VerifiedFormOfflinePolicy(GetBool(item, "enabled") ?? false, transports);
    }

    // ---------------------------------------------------------------------------------------------
    //  STAC: GET /stac/collections
    // ---------------------------------------------------------------------------------------------

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

    // Status zero means the server was unreachable, not a denial, and the probe omits the admin credential.
    private async Task<VerifiedAnonymousFetch> FetchAnonymousAsync(
        string path,
        CancellationToken cancellationToken,
        HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new VerifiedAnonymousFetch(0, false, null, null, null);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            var success = response.IsSuccessStatusCode;
            string? itemId = null;
            string? title = null;
            string? accessTier = null;

            if (success)
            {
                var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(payload);
                        var root = document.RootElement;
                        // The anonymous Share resolutions wrap the projection in {success,data,...}; the
                        // published-route read returns the view object directly. Handle both.
                        var body = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                            ? data
                            : root;
                        itemId = GetString(body, "itemId") ?? GetString(body, "publicationId");
                        title = GetString(body, "title");
                        accessTier = GetString(body, "accessTier");
                    }
                    catch (JsonException)
                    {
                        // Leave the projection fields null; the status code is the load-bearing assertion.
                    }
                }
            }

            return new VerifiedAnonymousFetch(status, success, itemId, title, accessTier);
        }
    }

    // A gated route returns 401, an absent route returns 404, and transport failure is status zero.
    private async Task<int> ProbeStatusAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return (int)response.StatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 0;
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

    private static long? GetLong(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
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
    IReadOnlyList<string> AvailableProtocols,
    VerifiedAccessPolicy? AccessPolicy = null);

public sealed record VerifiedAccessPolicy(
    bool? AllowAnonymous,
    bool? AllowAnonymousWrite,
    IReadOnlyList<string> AllowedRoles,
    IReadOnlyList<string> AllowedWriteRoles);

public sealed record VerifiedCatalogItem(
    string Id,
    string? Title,
    string? ItemType,
    string? Visibility);

public sealed record VerifiedPublicationRoute(
    string? PublicationId,
    string? RouteSlug,
    string? RoutePath,
    string? Kind,
    int? ActiveRevision,
    string? Visibility);

// VerifiedAnonymousFetch uses status zero for transport failure.
public sealed record VerifiedAnonymousFetch(
    int StatusCode,
    bool Granted,
    string? ItemId,
    string? Title,
    string? AccessTier);

public sealed record VerifiedTemporalCapability(
    string? ServiceId,
    int? LayerId,
    bool SupportsHistory,
    bool SupportsAsOf,
    string? TemporalColumn,
    string? CursorKind,
    long? CurrentGeneration);

public sealed record VerifiedTemporalChange(long? ObjectId, string? Operation);

public sealed record VerifiedTemporalAsOf(
    long? ResolvedGeneration,
    long? CurrentGeneration,
    string? RequestedCursorKind,
    IReadOnlyList<VerifiedTemporalChange> Changes)
{
    public int Count => Changes.Count;
}

public sealed record VerifiedReplica(
    string? ReplicaId,
    string? ReplicaName,
    string? ServiceId,
    string? SyncModel,
    long? LastSyncGeneration);

public sealed record VerifiedCatalogEndpoint(string? Key, string? Dialect, bool? Enabled, int? Entries);

public sealed record VerifiedCatalogRegistry(string? WorkspaceId, IReadOnlyList<VerifiedCatalogEndpoint> Endpoints);

public sealed record VerifiedCommentThread(
    string? ThreadId,
    string? FeatureLabel,
    string? LayerRef,
    bool Resolved,
    int? CommentCount,
    IReadOnlyList<string> Messages);

public sealed record VerifiedStudioContentVersion(
    string? ItemId,
    string? VersionId,
    int? VersionNumber,
    string? PackageKey,
    string? Family,
    string? SchemaVersion,
    string? ContentHash,
    string? Visibility,
    string? Route);

public sealed record VerifiedFormPackage(
    string? FormId,
    string? Title,
    string? ServiceId,
    int? LayerId,
    int? Version,
    string? Status);

public sealed record VerifiedFormOfflinePolicy(bool Enabled, IReadOnlyList<string> Transports);

// VersionRouteMounted and CapabilitiesRouteMounted stay separate from the nullable descriptor so an empty descriptor is not an absent route, and the record shows contract drift against the pinned server.
public sealed record VerifiedServerContract(
    string? Version,
    string? MetadataApiVersion,
    string? MetadataSchemaVersion,
    bool VersionRouteMounted,
    bool CapabilitiesRouteMounted);
