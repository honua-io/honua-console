using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class HonuaServerOperateTransitionDataSource : IOperateTransitionDataSource
{
    private const string MetadataResourcesContract = "GET /api/v1/admin/metadata/resources";

    private static readonly IReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse> EmptyServiceSettings =
        new ReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse>(
            new Dictionary<string, HonuaAdminServiceSettingsResponse>(StringComparer.OrdinalIgnoreCase));

    private readonly IHonuaAdminOperateClient _client;

    public HonuaServerOperateTransitionDataSource(IHonuaAdminOperateClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        // The landing page is the only surface that needs the whole workspace; fan out the
        // independent top-level reads, then the dependent layer/settings reads, in parallel.
        var connectionsTask = LoadConnectionsAsync(cancellationToken);
        var summariesTask = LoadServiceSummariesAsync(cancellationToken);
        var settingsChangesTask = LoadSettingsChangesAsync(cancellationToken);
        await Task.WhenAll(connectionsTask, summariesTask, settingsChangesTask).ConfigureAwait(false);

        var (connections, connectionStates) = await connectionsTask.ConfigureAwait(false);
        var (summaries, summaryStates) = await summariesTask.ConfigureAwait(false);
        var (settingsChanges, settingsChangeStates) = await settingsChangesTask.ConfigureAwait(false);

        var layerRowsTask = LoadLayerRowsAsync(connections, summaries, cancellationToken);
        var serviceSettingsTask = LoadServiceSettingsAsync(summaries, cancellationToken);
        await Task.WhenAll(layerRowsTask, serviceSettingsTask).ConfigureAwait(false);
        var (layerRows, layerStates) = await layerRowsTask.ConfigureAwait(false);
        var (serviceSettings, serviceSettingsStates) = await serviceSettingsTask.ConfigureAwait(false);

        var states = new List<OperateCapabilityState>();
        MergeStates(states, connectionStates);
        MergeStates(states, summaryStates);
        MergeStates(states, layerStates);
        AddStateOnce(states, ResourcesUnsupportedState());
        MergeStates(states, serviceSettingsStates);
        MergeStates(states, settingsChangeStates);

        var resources = BuildResourceEdits(layerRows);
        var services = MapServices(summaries, serviceSettings, layerRows);

        return new OperateTransitionWorkspace(
            connections,
            resources,
            services,
            settingsChanges,
            states);
    }

    public async Task<OperateConnectionsView> GetConnectionsViewAsync(CancellationToken cancellationToken = default)
    {
        var (connections, states) = await LoadConnectionsAsync(cancellationToken).ConfigureAwait(false);
        return new OperateConnectionsView(connections, states);
    }

    public async Task<OperateResourcesView> GetResourcesViewAsync(CancellationToken cancellationToken = default)
    {
        var connectionsTask = LoadConnectionsAsync(cancellationToken);
        var summariesTask = LoadServiceSummariesAsync(cancellationToken);
        await Task.WhenAll(connectionsTask, summariesTask).ConfigureAwait(false);
        var (connections, connectionStates) = await connectionsTask.ConfigureAwait(false);
        var (summaries, summaryStates) = await summariesTask.ConfigureAwait(false);

        var (layerRows, layerStates) = await LoadLayerRowsAsync(connections, summaries, cancellationToken)
            .ConfigureAwait(false);

        var states = new List<OperateCapabilityState>();
        MergeStates(states, connectionStates);
        MergeStates(states, summaryStates);
        MergeStates(states, layerStates);
        AddStateOnce(states, ResourcesUnsupportedState());

        return new OperateResourcesView(BuildResourceEdits(layerRows), states);
    }

    public Task<OperateServicesView> GetServicesViewAsync(CancellationToken cancellationToken = default) =>
        BuildServicesViewAsync(includeRuntimeSettings: true, cancellationToken);

    public Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default) =>
        BuildServicesViewAsync(includeRuntimeSettings: false, cancellationToken);

    public async Task<OperateSettingsView> GetSettingsViewAsync(CancellationToken cancellationToken = default)
    {
        var (settingsChanges, states) = await LoadSettingsChangesAsync(cancellationToken).ConfigureAwait(false);
        return new OperateSettingsView(settingsChanges, states);
    }

    // Services and layers share the same service/layer projection. The services surface needs the
    // per-service settings reads (runtime settings, publication slots); the layers surface does not,
    // so it skips those calls.
    private async Task<OperateServicesView> BuildServicesViewAsync(
        bool includeRuntimeSettings,
        CancellationToken cancellationToken)
    {
        var connectionsTask = LoadConnectionsAsync(cancellationToken);
        var summariesTask = LoadServiceSummariesAsync(cancellationToken);
        await Task.WhenAll(connectionsTask, summariesTask).ConfigureAwait(false);
        var (connections, connectionStates) = await connectionsTask.ConfigureAwait(false);
        var (summaries, summaryStates) = await summariesTask.ConfigureAwait(false);

        var layerRowsTask = LoadLayerRowsAsync(connections, summaries, cancellationToken);
        var serviceSettingsTask = includeRuntimeSettings
            ? LoadServiceSettingsAsync(summaries, cancellationToken)
            : null;

        await layerRowsTask.ConfigureAwait(false);
        if (serviceSettingsTask is not null)
        {
            await serviceSettingsTask.ConfigureAwait(false);
        }

        var (layerRows, layerStates) = await layerRowsTask.ConfigureAwait(false);

        var states = new List<OperateCapabilityState>();
        MergeStates(states, connectionStates);
        MergeStates(states, summaryStates);
        MergeStates(states, layerStates);

        var serviceSettings = EmptyServiceSettings;
        if (serviceSettingsTask is not null)
        {
            var (settings, settingsStates) = await serviceSettingsTask.ConfigureAwait(false);
            MergeStates(states, settingsStates);
            serviceSettings = settings;
        }

        var services = MapServices(summaries, serviceSettings, layerRows);

        return new OperateServicesView(services, states);
    }

    public async Task<OperateConnectionSummary?> FindConnectionAsync(
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var (connections, _) = await LoadConnectionsAsync(cancellationToken).ConfigureAwait(false);
        return connections.FirstOrDefault(
            connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperateResourceEditPreview?> FindResourceEditAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var view = await GetResourcesViewAsync(cancellationToken).ConfigureAwait(false);
        return view.ResourceEdits.FirstOrDefault(
            resource => string.Equals(resource.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<OperateServiceDetail?> FindServiceAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        var view = await GetServicesViewAsync(cancellationToken).ConfigureAwait(false);
        return view.Services.FirstOrDefault(
            service => string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(OperateConnectionSummary[] Connections, List<OperateCapabilityState> States)>
        LoadConnectionsAsync(CancellationToken cancellationToken)
    {
        var states = new List<OperateCapabilityState>();
        var connectionResult = await _client.ListConnectionsAsync(cancellationToken).ConfigureAwait(false);
        AddIssue(states, "Connections", connectionResult.Issue);
        var connections = (connectionResult.Data ?? [])
            .Select(MapConnection)
            .OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (connections, states);
    }

    private async Task<(HonuaAdminServiceSummary[] Summaries, List<OperateCapabilityState> States)>
        LoadServiceSummariesAsync(CancellationToken cancellationToken)
    {
        var states = new List<OperateCapabilityState>();
        var serviceResult = await _client.ListServicesAsync(cancellationToken).ConfigureAwait(false);
        AddIssue(states, "Services", serviceResult.Issue);
        var summaries = (serviceResult.Data ?? [])
            .Where(service => !string.IsNullOrWhiteSpace(service.ServiceName))
            .OrderBy(service => service.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (summaries, states);
    }

    private async Task<(IReadOnlyList<OperateSettingsChange> Changes, List<OperateCapabilityState> States)>
        LoadSettingsChangesAsync(CancellationToken cancellationToken)
    {
        var states = new List<OperateCapabilityState>();
        var versionTask = _client.GetVersionAsync(cancellationToken);
        var capabilitiesTask = _client.GetCapabilitiesAsync(cancellationToken);
        var licenseTask = _client.GetLicenseStatusAsync(cancellationToken);
        var apiKeysTask = _client.ListApiKeysAsync(cancellationToken);
        var oidcProvidersTask = _client.ListOidcProvidersAsync(cancellationToken);

        await Task.WhenAll(versionTask, capabilitiesTask, licenseTask, apiKeysTask, oidcProvidersTask)
            .ConfigureAwait(false);

        var changes = MapSettings(
                await versionTask.ConfigureAwait(false),
                await capabilitiesTask.ConfigureAwait(false),
                await licenseTask.ConfigureAwait(false),
                await apiKeysTask.ConfigureAwait(false),
                await oidcProvidersTask.ConfigureAwait(false),
                states)
            .ToArray();
        return (changes, states);
    }

    private static void MergeStates(
        List<OperateCapabilityState> target,
        IEnumerable<OperateCapabilityState> source)
    {
        foreach (var state in source)
        {
            AddStateOnce(target, state);
        }
    }

    private static OperateCapabilityState ResourcesUnsupportedState() =>
        new(
            "Resources",
            "Unsupported",
            $"{MetadataResourcesContract} plus resource edit validation/blast-radius projection",
            "honua-server does not currently expose a Metadata v2 resource-edit preview contract for validation issues, saved maps, share links, or generated-app blast radius.");

    private async Task<(LayerRow[] Rows, List<OperateCapabilityState> States)> LoadLayerRowsAsync(
        IReadOnlyList<OperateConnectionSummary> connections,
        IReadOnlyList<HonuaAdminServiceSummary> services,
        CancellationToken cancellationToken)
    {
        var states = new List<OperateCapabilityState>();
        if (connections.Count == 0)
        {
            return ([], states);
        }

        var serviceScopes = CreateLayerServiceScopes(services);
        var layerTasks = connections
            .SelectMany(connection => serviceScopes.Select(async serviceName => (
                Connection: connection,
                ServiceName: serviceName,
                Result: await _client.ListConnectionLayersAsync(
                        connection.Id,
                        serviceName,
                        cancellationToken)
                    .ConfigureAwait(false))))
            .ToArray();

        await Task.WhenAll(layerTasks).ConfigureAwait(false);

        var rows = new List<LayerRow>();
        var seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layerTask in layerTasks)
        {
            var (connection, serviceName, result) = await layerTask.ConfigureAwait(false);
            AddIssue(states, "Layers", result.Issue);

            foreach (var layer in result.Data ?? [])
            {
                var effectiveServiceName = Coalesce(layer.ServiceName, serviceName, "default");
                var rowKey = string.Join(
                    '\u001f',
                    connection.Id,
                    layer.LayerId.ToString(CultureInfo.InvariantCulture),
                    effectiveServiceName);
                if (!seenRows.Add(rowKey))
                {
                    continue;
                }

                var normalizedLayer = layer with { ServiceName = effectiveServiceName };
                var resource = MapResource(connection, normalizedLayer);
                rows.Add(new LayerRow(
                    connection,
                    normalizedLayer,
                    resource,
                    new OperateServiceLayerProjection(
                        normalizedLayer.LayerId,
                        Coalesce(normalizedLayer.LayerName, $"Layer {normalizedLayer.LayerId}"),
                        Coalesce(normalizedLayer.GeometryType, "Unknown"),
                        resource.ResourceId,
                        resource.Name)));
            }
        }

        return (rows.ToArray(), states);
    }

    private static IReadOnlyList<string?> CreateLayerServiceScopes(IReadOnlyList<HonuaAdminServiceSummary> services)
    {
        var serviceScopes = new List<string?> { null };
        var serviceNames = services
            .Select(service => service.ServiceName?.Trim())
            .Where(serviceName => !string.IsNullOrWhiteSpace(serviceName))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var serviceName in serviceNames)
        {
            if (string.Equals(serviceName, "default", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            serviceScopes.Add(serviceName);
        }

        return serviceScopes;
    }

    private async Task<(IReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse> Settings, List<OperateCapabilityState> States)>
        LoadServiceSettingsAsync(
            IReadOnlyList<HonuaAdminServiceSummary> services,
            CancellationToken cancellationToken)
    {
        var states = new List<OperateCapabilityState>();
        var settingsTasks = services
            .Where(service => !string.IsNullOrWhiteSpace(service.ServiceName))
            .Select(async service => (
                ServiceName: service.ServiceName!,
                Result: await _client.GetServiceSettingsAsync(service.ServiceName!, cancellationToken)
                    .ConfigureAwait(false)))
            .ToArray();

        await Task.WhenAll(settingsTasks).ConfigureAwait(false);

        var settings = new Dictionary<string, HonuaAdminServiceSettingsResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var settingsTask in settingsTasks)
        {
            var (serviceName, result) = await settingsTask.ConfigureAwait(false);
            AddIssue(states, "Services", result.Issue);
            if (result.Data is null)
            {
                continue;
            }

            settings[serviceName] = result.Data;
            if (result.Data.TimeInfo is null)
            {
                AddStateOnce(
                    states,
                    new OperateCapabilityState(
                        "Services",
                        "Unsupported",
                        "GET /api/v1/admin/services/{serviceName}/settings timeInfo",
                        "honua-server reports service-scope TimeInfo as unavailable in the Metadata v2 graph; use the per-layer metadata contract when it lands in the Console SDK projection."));
            }
        }

        return (new ReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse>(settings), states);
    }

    private static IReadOnlyList<OperateResourceEditPreview> BuildResourceEdits(IReadOnlyList<LayerRow> layerRows)
    {
        return layerRows
            .GroupBy(row => row.Resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var resource = group
                    .Select(row => row.Resource)
                    .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(candidate => candidate.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .First();
                var blastRadius = resource.BlastRadius;

                return resource with
                {
                    BlastRadius = blastRadius with
                    {
                        CatalogItems = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.CatalogItems)),
                        Services = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.Services)),
                        Layers = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.Layers)),
                        SavedMaps = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.SavedMaps)),
                        ShareLinks = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.ShareLinks)),
                        GeneratedApps = DistinctOrdered(group.SelectMany(row => row.Resource.BlastRadius.GeneratedApps))
                    }
                };
            })
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(resource => resource.ResourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> DistinctOrdered(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private IReadOnlyList<OperateServiceDetail> MapServices(
        IReadOnlyList<HonuaAdminServiceSummary> summaries,
        IReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse> serviceSettings,
        IReadOnlyList<LayerRow> layerRows)
    {
        // Services and layers render through the service summary list, but each published-layer row
        // carries its own service name. When the admin services endpoint reports no rows (or omits a
        // service that still owns published layers), derive the missing service entries from the layer
        // rows so the layers do not disappear from the services and layers surfaces. These are projected
        // from live published-layer data, not fabricated rows for an unsupported contract.
        return CombineServiceSummaries(summaries, layerRows)
            .Select(service => MapService(service, serviceSettings, layerRows))
            .ToArray();
    }

    private static IReadOnlyList<HonuaAdminServiceSummary> CombineServiceSummaries(
        IReadOnlyList<HonuaAdminServiceSummary> summaries,
        IReadOnlyList<LayerRow> layerRows)
    {
        var knownServiceNames = summaries
            .Select(summary => summary.ServiceName?.Trim())
            .Where(serviceName => !string.IsNullOrWhiteSpace(serviceName))
            .Select(serviceName => serviceName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var derivedServices = layerRows
            .GroupBy(row => Coalesce(row.Source.ServiceName, "default"), StringComparer.OrdinalIgnoreCase)
            .Where(group => !knownServiceNames.Contains(group.Key))
            .Select(group => new HonuaAdminServiceSummary
            {
                ServiceName = group.Key,
                LayerCount = group.Count(),
                EnabledProtocols = []
            });

        return summaries
            .Concat(derivedServices)
            .OrderBy(summary => summary.ServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private OperateServiceDetail MapService(
        HonuaAdminServiceSummary service,
        IReadOnlyDictionary<string, HonuaAdminServiceSettingsResponse> serviceSettings,
        IReadOnlyList<LayerRow> layerRows)
    {
        var serviceName = service.ServiceName!;
        serviceSettings.TryGetValue(serviceName, out var settings);
        var protocols = ((settings?.EnabledProtocols ?? []).Count > 0 ? settings!.EnabledProtocols : service.EnabledProtocols ?? [])
            .Where(protocol => !string.IsNullOrWhiteSpace(protocol))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var layers = layerRows
            .Where(row => string.Equals(
                Coalesce(row.Source.ServiceName, "default"),
                serviceName,
                StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Projection)
            .OrderBy(layer => layer.LayerId)
            .ToArray();

        return new OperateServiceDetail(
            serviceName,
            Coalesce(service.Description, serviceName),
            protocols.Length == 0 ? "Geo service" : string.Join(", ", protocols),
            service.LayerCount > 0 ? "Configured" : "No layers",
            "Canonical metadata is owned by honua-server Metadata v2 resources. Service settings control runtime protocols, access policy, and exposure.",
            layers,
            MapRuntimeSettings(service, settings, protocols),
            MapPublicationSlots(serviceName, protocols));
    }

    private IReadOnlyList<OperateRuntimeSetting> MapRuntimeSettings(
        HonuaAdminServiceSummary service,
        HonuaAdminServiceSettingsResponse? settings,
        IReadOnlyList<string> protocols)
    {
        var runtimeSettings = new List<OperateRuntimeSetting>
        {
            new(
                "enabledProtocols",
                protocols.Count == 0 ? "None reported" : string.Join(", ", protocols),
                "Service runtime protocol exposure",
                RequiresRestart: false),
            new(
                "layerCount",
                service.LayerCount.ToString(CultureInfo.InvariantCulture),
                "Published service layer projection",
                RequiresRestart: false)
        };

        if (settings?.AccessPolicy is { } accessPolicy)
        {
            runtimeSettings.Add(new OperateRuntimeSetting(
                "accessPolicy",
                FormatAccessPolicy(accessPolicy),
                "Service access policy",
                RequiresRestart: false));
        }

        if (settings?.MapServer is { } mapServer)
        {
            runtimeSettings.Add(new OperateRuntimeSetting(
                "mapServer.defaultFormat",
                Coalesce(mapServer.DefaultFormat, "Not reported"),
                "MapServer runtime defaults",
                RequiresRestart: false));
            runtimeSettings.Add(new OperateRuntimeSetting(
                "mapServer.maxImageSize",
                $"{mapServer.MaxImageWidth?.ToString(CultureInfo.InvariantCulture) ?? "?"} x {mapServer.MaxImageHeight?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
                "MapServer runtime defaults",
                RequiresRestart: false));
        }

        return runtimeSettings;
    }

    private IReadOnlyList<OperatePublicationSlot> MapPublicationSlots(
        string serviceName,
        IReadOnlyList<string> protocols)
    {
        if (protocols.Count == 0)
        {
            return [];
        }

        return protocols
            .Select(protocol => new OperatePublicationSlot(
                protocol,
                BuildProtocolUrl(serviceName, protocol),
                "Exposed by honua-server"))
            .ToArray();
    }

    private IEnumerable<OperateSettingsChange> MapSettings(
        HonuaAdminEndpointResult<HonuaAdminVersionResponse> versionResult,
        HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse> capabilitiesResult,
        HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse> licenseResult,
        HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]> apiKeysResult,
        HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]> oidcProvidersResult,
        List<OperateCapabilityState> states)
    {
        AddIssue(states, "Settings", versionResult.Issue);
        AddIssue(states, "Settings", capabilitiesResult.Issue);
        AddIssue(states, "Settings", licenseResult.Issue);
        AddIssue(states, "Settings", apiKeysResult.Issue);
        AddIssue(states, "Settings", oidcProvidersResult.Issue);

        if (versionResult.Data is { } version)
        {
            yield return new OperateSettingsChange(
                "admin-version",
                "Runtime",
                "Admin API version",
                $"Server {Coalesce(version.Version, "unknown")} with Metadata API {Coalesce(version.MetadataApiVersion, "unknown")}.",
                "Admin API compatibility",
                RequiresRestart: false,
                "No restart; this row is read from the live server.",
                "GET /api/v1/admin/version.");
        }

        if (capabilitiesResult.Data is { } capabilities)
        {
            yield return new OperateSettingsChange(
                "admin-capabilities",
                "Runtime",
                "Metadata schema capability",
                $"Metadata schema {Coalesce(capabilities.MetadataSchemaVersion, "unknown")} reported by honua-server.",
                "Metadata v2 compatibility",
                RequiresRestart: false,
                "No restart; this row is read from the live server.",
                "GET /api/v1/admin/capabilities.");
        }

        if (licenseResult.Data is { } license)
        {
            var entitlementCount = (license.Entitlements ?? []).Count(entitlement => entitlement.IsActive == true);
            yield return new OperateSettingsChange(
                "license",
                "License",
                "License status",
                $"{Coalesce(license.Edition, "Unknown")} edition, {Coalesce(license.ValidationState, license.IsValid == true ? "valid" : "invalid")}, {entitlementCount} active entitlements.",
                "License cache and edition gates",
                RequiresRestart: false,
                "No restart reported by the license status endpoint.",
                "GET /api/v1/admin/license.");
        }

        if (apiKeysResult.Data is { } apiKeys)
        {
            var activeCount = apiKeys.Count(key => string.Equals(key.Status, "active", StringComparison.OrdinalIgnoreCase));
            yield return new OperateSettingsChange(
                "api-keys",
                "Identity",
                "Admin API keys",
                $"{activeCount} active of {apiKeys.Length} server-managed API keys. Secret values are not returned by list responses.",
                "Admin API key authentication",
                RequiresRestart: false,
                "No restart; created and rotated key values are one-time server reveals.",
                "GET /api/v1/admin/api-keys.");
        }

        if (oidcProvidersResult.Data is { } providers)
        {
            var enabledCount = providers.Count(provider => provider.Enabled == true);
            yield return new OperateSettingsChange(
                "identity-provider",
                "Identity",
                "OIDC provider metadata",
                $"{enabledCount} enabled of {providers.Length} configured OIDC providers.",
                "Identity policy and sign-in discovery",
                RequiresRestart: false,
                "No restart reported by the OIDC provider list endpoint.",
                "GET /api/v1/admin/oidc/providers.");
        }

        AddStateOnce(
            states,
            new OperateCapabilityState(
                "Settings",
                "Unsupported",
                "CORS admin read/write contract",
                "honua-server configures CORS from process configuration and does not expose an operator settings API for allowed origins."));

        AddStateOnce(
            states,
            new OperateCapabilityState(
                "Settings",
                "Unsupported",
                "Catalog endpoint visibility admin contract",
                "honua-server does not currently expose a Console admin contract for catalog endpoint visibility changes."));
    }

    private static OperateConnectionSummary MapConnection(HonuaAdminConnectionSummary connection)
    {
        var id = connection.ConnectionId == Guid.Empty
            ? NormalizeToken(Coalesce(connection.Name, connection.Host, "connection"))
            : connection.ConnectionId.ToString("D");
        var status = MapConnectionStatus(connection);
        var diagnostic = CreateConnectionDiagnostic(connection, status);

        return new OperateConnectionSummary(
            id,
            Coalesce(connection.Name, connection.Host, id),
            Coalesce(connection.Provider, "PostgreSQL/PostGIS"),
            FormatTarget(connection),
            Coalesce(connection.Username, "Not reported"),
            status,
            FormatTimestamp(connection.LastHealthCheck),
            diagnostic?.ToSafeDiagnostic());
    }

    private static OperateConnectionDiagnostic? CreateConnectionDiagnostic(
        HonuaAdminConnectionSummary connection,
        string status)
    {
        if (string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Untested", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var failureCode = NormalizeToken(Coalesce(connection.HealthStatus, status)).ToUpperInvariant();
        return new OperateConnectionDiagnostic(
            status,
            failureCode,
            $"honua-server reports connection health status '{Coalesce(connection.HealthStatus, status)}'.",
            [
                new OperateDiagnosticSignal(
                    "server-health",
                    status,
                    $"Connection metadata was read from GET /api/v1/admin/connections for {Coalesce(connection.Name, connection.Host, "connection")}.")
            ],
            [
                "Run the server-side connection test endpoint before using this source for publication."
            ],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["connectionId"] = connection.ConnectionId.ToString("D"),
                ["provider"] = Coalesce(connection.Provider, "not-reported"),
                ["storageType"] = Coalesce(connection.StorageType, "not-reported"),
                ["sslMode"] = Coalesce(connection.SslMode, "not-reported")
            });
    }

    private static OperateResourceEditPreview MapResource(
        OperateConnectionSummary connection,
        HonuaAdminPublishedLayerSummary layer)
    {
        var layerName = Coalesce(layer.LayerName, $"Layer {layer.LayerId}");
        var source = string.IsNullOrWhiteSpace(layer.Schema) && string.IsNullOrWhiteSpace(layer.Table)
            ? connection.Name
            : $"{connection.Name} / {Coalesce(layer.Schema, "schema")}.{Coalesce(layer.Table, "table")}";
        var resourceId = $"{NormalizeToken(connection.Id)}-layer-{layer.LayerId.ToString(CultureInfo.InvariantCulture)}";
        var serviceName = Coalesce(layer.ServiceName, "default");

        return new OperateResourceEditPreview(
            resourceId,
            layerName,
            source,
            "Server-backed published layer metadata; draft edit preview is waiting on the Metadata v2 resource edit contract.",
            layer.Enabled == false ? "Disabled" : "Published",
            new OperateBlastRadius(
                CatalogItems: [],
                Services: [serviceName],
                Layers: [layerName],
                SavedMaps: [],
                ShareLinks: [],
                GeneratedApps: []),
            [
                new OperateValidationIssue(
                    "Info",
                    "server-contract",
                    "Live publication metadata is available, but draft validation and downstream blast-radius details are not projected by honua-server yet.",
                    "Keep edits blocked in Console until the resource edit validation contract lands.")
            ],
            ["Overview", "Source", "Fields", "Validation"]);
    }

    private string BuildProtocolUrl(string serviceName, string protocol)
    {
        var escapedService = Uri.EscapeDataString(serviceName);
        var trimmedBase = _client.BaseUri.ToString().TrimEnd('/');

        return protocol switch
        {
            "FeatureServer" => $"{trimmedBase}/rest/services/{escapedService}/FeatureServer",
            "MapServer" => $"{trimmedBase}/rest/services/{escapedService}/MapServer",
            "Stac" => $"{trimmedBase}/stac",
            _ => $"{trimmedBase}/rest/services/{escapedService}/{Uri.EscapeDataString(protocol)}"
        };
    }

    private static string FormatTarget(HonuaAdminConnectionSummary connection)
    {
        var host = Coalesce(connection.Host, "host-not-reported");
        var port = connection.Port is { } value ? $":{value.ToString(CultureInfo.InvariantCulture)}" : string.Empty;
        var database = string.IsNullOrWhiteSpace(connection.DatabaseName) ? string.Empty : $"/{connection.DatabaseName}";
        return $"{host}{port}{database}";
    }

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Not tested";

    private static string MapConnectionStatus(HonuaAdminConnectionSummary connection)
    {
        if (connection.IsActive == false)
        {
            return "Disabled";
        }

        var healthStatus = connection.HealthStatus?.Trim();
        // A connection that has never been health-checked reports null/empty or the literal "Unknown".
        // That is NOT a failure — render it as a neutral "Untested" state, not a red "Failed" badge.
        if (string.IsNullOrWhiteSpace(healthStatus)
            || string.Equals(healthStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Untested";
        }

        if (string.Equals(healthStatus, "Healthy", StringComparison.OrdinalIgnoreCase))
        {
            return "Passed";
        }

        if (string.Equals(healthStatus, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            return "Warning";
        }

        return "Failed";
    }

    private static string FormatAccessPolicy(HonuaAdminAccessPolicyResponse accessPolicy)
    {
        var read = accessPolicy.AllowAnonymous == true ? "anonymous read" : "authenticated read";
        var write = accessPolicy.AllowAnonymousWrite == true ? "anonymous write" : "authenticated write";
        // AllowedRoles defaults to [] but the server can send an explicit JSON null, which overrides the
        // initializer — guard against null so a service with a null role allow-list doesn't 500 the surface.
        var allowedRoles = accessPolicy.AllowedRoles;
        var roles = allowedRoles is null || allowedRoles.Count == 0
            ? "no role allow-list"
            : $"roles: {string.Join(", ", allowedRoles)}";

        return $"{read}, {write}, {roles}";
    }

    private static void AddIssue(
        ICollection<OperateCapabilityState> states,
        string surface,
        HonuaAdminEndpointIssue? issue)
    {
        if (issue is null)
        {
            return;
        }

        AddStateOnce(
            states,
            new OperateCapabilityState(
                surface,
                issue.State,
                issue.Contract,
                issue.StatusCode is null ? issue.Detail : $"{issue.Detail} HTTP {issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}."));
    }

    private static void AddStateOnce(
        ICollection<OperateCapabilityState> states,
        OperateCapabilityState state)
    {
        if (states.Any(existing =>
                string.Equals(existing.Surface, state.Surface, StringComparison.Ordinal)
                && string.Equals(existing.State, state.State, StringComparison.Ordinal)
                && string.Equals(existing.Contract, state.Contract, StringComparison.Ordinal)))
        {
            return;
        }

        states.Add(state);
    }

    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string NormalizeToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is '-' or '_')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character) || character is ':' or '/' or '.')
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "item" : normalized;
    }

    private sealed record LayerRow(
        OperateConnectionSummary Connection,
        HonuaAdminPublishedLayerSummary Source,
        OperateResourceEditPreview Resource,
        OperateServiceLayerProjection Projection);
}
