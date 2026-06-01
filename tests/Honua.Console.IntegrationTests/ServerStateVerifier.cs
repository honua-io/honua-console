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
    //  Console catalog detail: GET /api/v1/console/content/{id}
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads a single console content item's catalog projection (title, itemType, visibility) independently
    /// of the operation that created it. Returns <c>null</c> when the item is absent or the surface is
    /// unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads the admin content-publication route state (route slug/path, kind, active revision, visibility)
    /// independently of the console publish operation. Returns <c>null</c> when absent / unavailable.
    /// </summary>
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

    /// <summary>
    /// Fetches a published artifact by its route slug through the public <c>/api/v1/published</c> surface as
    /// an UNAUTHENTICATED client (no admin key). Returns the HTTP status and the resolved title/revision so a
    /// test can assert a public publication is actually reachable anonymously (200) while a private one is
    /// denied (401/403/404) — the independent proof the route landed and enforces visibility.
    /// </summary>
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

    /// <summary>
    /// Resolves a public-link token through the anonymous Console Share surface
    /// (<c>GET /api/v1/console/share/link/{token}</c>) as an UNAUTHENTICATED client. A granted token returns
    /// 200 with the item's presentation projection; an unknown/expired/revoked/forbidden token returns 404
    /// with a constant denial body. This is the independent proof a minted token actually grants the scope.
    /// </summary>
    public Task<VerifiedAnonymousFetch> ResolvePublicLinkAnonymouslyAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/link/{Uri.EscapeDataString(token)}", cancellationToken);

    /// <summary>
    /// Resolves a public-indexed content item through the anonymous Console Share surface
    /// (<c>GET /api/v1/console/share/content/{id}</c>) as an UNAUTHENTICATED client. A public-indexed item
    /// returns 200; a private/missing/no-longer-public item returns 404. The independent proof an access-tier
    /// raise actually grants anonymous content access.
    /// </summary>
    public Task<VerifiedAnonymousFetch> ResolvePublicContentAnonymouslyAsync(
        string itemId,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/content/{Uri.EscapeDataString(itemId)}", cancellationToken);

    /// <summary>
    /// Redeems an embed token through the anonymous Console Share surface
    /// (<c>POST /api/v1/console/share/embed/{token}/redeem</c>) as an UNAUTHENTICATED client. A live embed
    /// token returns 200 with the embed resolution; an expired/disabled token returns 404.
    /// </summary>
    public Task<VerifiedAnonymousFetch> RedeemEmbedTokenAnonymouslyAsync(
        string token,
        CancellationToken cancellationToken = default) =>
        FetchAnonymousAsync($"/api/v1/console/share/embed/{Uri.EscapeDataString(token)}/redeem", cancellationToken, HttpMethod.Post);

    // ---------------------------------------------------------------------------------------------
    //  Temporal capability: GET /api/v1/temporal/services/{serviceId}/layers/{layerId}/capabilities
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the server-owned temporal capability descriptor for a layer (honua-server#1166) — whether the
    /// layer supports history / as-of reads, the backing temporal column + cursor kind, and the current
    /// generation. This is the canonical server temporal read, independent of the console temporal client.
    /// Returns <c>null</c> when the capability route is absent (404) so a caller can distinguish a
    /// route-mounted-but-unsupported layer (200 with supportsHistory=false) from a missing contract.
    /// </summary>
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

    /// <summary>
    /// Performs an as-of read for a layer at a generation cursor (honua-server#1166). Returns the resolved /
    /// current generation and the collapsed feature-change set (object ids + net operations). Returns
    /// <c>null</c> when the layer does not support temporal reads (the server answers 409 Conflict) or the
    /// route is absent, so the caller can assert the unsupported path independently.
    /// </summary>
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

    /// <summary>
    /// Reads the named-replica registry for a service (honua-server#1167) and returns each replica's id /
    /// name / sync model / last-sync generation. This is the canonical server replica read, independent of
    /// the console replica list. Returns an empty list when the service exposes no replicas or the surface
    /// is unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads a single named replica's detail (honua-server#1167). Returns <c>null</c> for a foreign /
    /// unknown replica id (the server answers 404) or when the surface is unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads the server-owned catalog discovery-endpoints registry (honua-server#1279) and returns the
    /// endpoint keys/dialects + per-endpoint entry counts. This is the canonical server registry read,
    /// independent of the console catalogs data source. Returns <c>null</c> when the registry route is absent.
    /// </summary>
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

    /// <summary>
    /// Probes an admin-gated route as a genuinely UNAUTHENTICATED client and returns the raw HTTP status.
    /// This is the strong route-mounted discriminator (vs. asserting an opaque "Unsupported" projection): a
    /// mounted + gated route rejects an anonymous request with 401 (matching the server's
    /// <c>AnonymousRequests_AreRejectedWithoutAdminAuthorization</c> tests), while an absent route returns
    /// 404. Status 0 signals the server was unreachable.
    /// </summary>
    public Task<int> ProbeAdminRouteAnonymousStatusAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ProbeStatusAsync(path, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    //  Collaboration comments: GET /api/v1/console/maps/{mapId}/collab/comments (+ /activity)
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the durable comment threads for a map through the server collaboration API (honua-server#1278),
    /// independently of the console collaboration data source. Returns each thread's id / feature label /
    /// resolved flag / message count + bodies. Returns an empty list when the surface is unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads the durable collaboration activity feed for a map through the server collaboration API
    /// (honua-server#1278), independently of the console data source. Returns the activity action strings.
    /// </summary>
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

    /// <summary>
    /// Reads a single immutable Studio content-item version back through the canonical server content API
    /// (honua-server#1180/#1183), independently of the console builder data source: the package family/kind,
    /// the envelope schema version, the integer version number, the content hash, and the publication-intent
    /// visibility/route carried on the frozen envelope. This is the round-trip proof that a Studio
    /// authoring→publish (map / dashboard) actually landed a content version with the right shape, read via
    /// a DIFFERENT client than the one the publish went through (plan §4 rule #2). Returns <c>null</c> when the
    /// version is absent (404) or the surface is unavailable.
    /// </summary>
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

    /// <summary>
    /// Lists the immutable Studio content-item versions for an item through the canonical server content API,
    /// independently of the console builder. Returns the integer version numbers present, so a test can prove a
    /// freshly cut version actually landed (and that a republish advanced to a second version). Empty when the
    /// item is absent or the surface is unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads a form package's current server-side state back through the admin form-package API
    /// (honua-server#1184), independently of the console form builder data source: the form id, title, target
    /// service/layer, version number, and the lifecycle status (draft / published). This is the round-trip
    /// proof a form authoring→publish actually flipped the published version on the server. Returns
    /// <c>null</c> when the form is absent (404) or the surface is unavailable.
    /// </summary>
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

    /// <summary>
    /// Reads a published form's runtime offline-sync policy through the canonical RUNTIME contract
    /// (<c>GET /api/v1/forms/packages/{formId}/offline-policy</c>, honua-server#1184) — a genuinely different
    /// surface than the admin publish route the operation went through. Returns whether offline is enabled and
    /// the available transports, or <c>null</c> when the form has no published version / the route is absent.
    /// </summary>
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

    /// <summary>
    /// Issues a request WITHOUT the admin API key (a genuinely anonymous client) and projects the HTTP status
    /// plus, on success, a few presentation fields parsed defensively from the response body. Transport
    /// failures surface as status 0 so a caller can distinguish "server unreachable" from a real denial.
    /// </summary>
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

    /// <summary>
    /// Issues a request WITHOUT the admin API key and returns the raw HTTP status code (0 on transport
    /// failure). Used for the 401-vs-404 route-mounted discriminator: a gated route returns 401 while an
    /// absent route returns 404.
    /// </summary>
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
    IReadOnlyList<string> AvailableProtocols);

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

/// <summary>
/// Result of an UNAUTHENTICATED fetch against a public/anonymous surface: the HTTP status (0 on transport
/// failure), whether it was a 2xx grant, and the presentation fields parsed on success. Lets a test assert
/// "an anonymous client is granted (200) / denied (4xx) access" without trusting the operation's own path.
/// </summary>
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

/// <summary>
/// An immutable Studio content-item version read back through the canonical server content API: the content
/// item + version ids, the integer version number, the package key, the envelope family (map/dashboard/…),
/// the envelope schema version, the content hash, and the publication-intent visibility/route. Lets a test
/// assert a Studio authoring→publish landed the right content shape independently of the publish path.
/// </summary>
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

/// <summary>
/// A form package's server-side state read back through the admin form-package API: the form id, title,
/// target service/layer, version number, and lifecycle status (draft / published). Lets a test assert a form
/// authoring→publish flipped the published version with the right shape independently of the publish path.
/// </summary>
public sealed record VerifiedFormPackage(
    string? FormId,
    string? Title,
    string? ServiceId,
    int? LayerId,
    int? Version,
    string? Status);

/// <summary>
/// A published form's runtime offline-sync policy read back through the runtime offline-policy contract (a
/// different surface than the admin publish route): whether offline use is enabled and the available
/// transports. Lets a test prove the publish landed a resolvable runtime policy, not just an admin row.
/// </summary>
public sealed record VerifiedFormOfflinePolicy(bool Enabled, IReadOnlyList<string> Transports);
