using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateTransitionDataSourceTests
{
    [Fact]
    public void RuntimeShellRegistrationDoesNotUseSeededInMemoryOperateData()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell();

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<IOperateTransitionDataSource>();

        Assert.IsNotType<InMemoryOperateTransitionDataSource>(dataSource);
        Assert.IsType<UnsupportedOperateTransitionDataSource>(dataSource);
    }

    [Fact]
    public void ConfiguredShellRegistrationUsesHonuaServerOperateData()
    {
        var services = new ServiceCollection();

        services.AddHonuaConsoleShell("https://server.example", "test-api-key");

        using var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<IOperateTransitionDataSource>();

        Assert.IsType<HonuaServerOperateTransitionDataSource>(dataSource);
    }

    [Fact]
    public async Task MissingServerBindingReturnsExplicitUnsupportedState()
    {
        var dataSource = new UnsupportedOperateTransitionDataSource();

        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Empty(workspace.Connections);
        Assert.Contains(
            workspace.CapabilityStates,
            state => state.State == "Missing binding"
                && state.Contract == "Honua:Server:BaseUrl");
    }

    [Fact]
    public async Task HonuaServerDataSourceMapsLiveAdminResponsesAndMissingContracts()
    {
        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new JsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionId}}","name":"Live PostGIS","host":"db.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","lastHealthCheck":"2026-05-24T10:00:00Z","storageType":"managed","sslMode":"Require"}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?serviceName=planning"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","description":"Fixture layer","geometryType":"Polygon","fieldCount":5,"enabled":true,"serviceName":"planning"}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?serviceName=public"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","description":"Fixture layer","geometryType":"Polygon","fieldCount":5,"enabled":true,"serviceName":"public"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[{"serviceName":"planning","description":"Planning Service","layerCount":1,"enabledProtocols":["FeatureServer","MapServer"]},{"serviceName":"public","description":"Public Service","layerCount":1,"enabledProtocols":["FeatureServer"]}]}
                """,
            ["/api/v1/admin/services/planning/settings"] = """
                {"success":true,"data":{"serviceName":"planning","enabledProtocols":["FeatureServer"],"availableProtocols":["FeatureServer","MapServer"],"accessPolicy":{"allowAnonymous":true,"allowAnonymousWrite":false,"allowedRoles":["operator"],"allowedWriteRoles":[]},"mapServer":{"maxImageWidth":4096,"maxImageHeight":4096,"defaultFormat":"png","maxFeaturesPerLayer":1000}}}
                """,
            ["/api/v1/admin/services/public/settings"] = """
                {"success":true,"data":{"serviceName":"public","enabledProtocols":["FeatureServer"],"availableProtocols":["FeatureServer"],"accessPolicy":{"allowAnonymous":true,"allowAnonymousWrite":false,"allowedRoles":[],"allowedWriteRoles":[]},"mapServer":{"maxImageWidth":2048,"maxImageHeight":2048,"defaultFormat":"png","maxFeaturesPerLayer":1000}}}
                """,
            ["/api/v1/admin/version"] = """
                {"success":true,"data":{"version":"1.2.3","metadataApiVersion":"v2","metadataSchemaVersion":"2026-05","serverTime":"2026-05-24T10:00:00Z"}}
                """,
            ["/api/v1/admin/capabilities"] = """
                {"success":true,"data":{"metadataApiVersion":"v2","metadataSchemaVersion":"2026-05","serverVersion":"1.2.3"}}
                """,
            ["/api/v1/admin/license/"] = """
                {"success":true,"data":{"edition":"Community","isValid":true,"validationState":"valid","entitlements":[{"key":"core","name":"Core","isActive":true}]}}
                """,
            ["/api/v1/admin/api-keys/"] = """
                {"success":true,"data":[{"id":"22222222-2222-2222-2222-222222222222","name":"automation","keyPrefix":"hnua_abc","status":"active","permissions":["admin"],"createdAt":"2026-05-24T10:00:00Z","updatedAt":"2026-05-24T10:00:00Z"}]}
                """,
            ["/api/v1/admin/oidc/providers/"] = """
                {"success":true,"data":[{"providerId":"33333333-3333-3333-3333-333333333333","name":"Tenant OIDC","providerType":"Generic","authority":"https://identity.example","clientId":"console","enabled":true,"isHealthy":true,"createdAt":"2026-05-24T10:00:00Z","updatedAt":"2026-05-24T10:00:00Z"}]}
                """
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://server.example")
        };
        var client = new HonuaAdminOperateHttpClient(
            httpClient,
            new HonuaAdminOperateClientOptions(new Uri("https://server.example"), "test-api-key"));
        var dataSource = new HonuaServerOperateTransitionDataSource(client);

        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Contains(workspace.Connections, connection => connection.Name == "Live PostGIS" && connection.Status == "Passed");
        var resource = Assert.Single(workspace.ResourceEdits, candidate => candidate.Name == "Console Parcels");
        Assert.Equal("Published", resource.ValidationState);
        Assert.Equal(["planning", "public"], resource.BlastRadius.Services);
        Assert.Contains(workspace.Services, service => service.Name == "planning" && service.Layers.Any(layer => layer.Name == "Console Parcels"));
        Assert.Contains(workspace.Services, service => service.Name == "public" && service.Layers.Any(layer => layer.CanonicalResourceId == resource.ResourceId));
        Assert.Contains(workspace.SettingsChanges, change => change.Id == "license" && change.ProposedChange.Contains("Community", StringComparison.Ordinal));
        Assert.Contains(
            workspace.CapabilityStates,
            state => state.Surface == "Resources"
                && state.State == "Unsupported"
                && state.Contract.Contains("/metadata/resources", StringComparison.Ordinal));
        Assert.Contains(
            workspace.CapabilityStates,
            state => state.Surface == "Settings"
                && state.Contract.Contains("CORS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConnectionsViewFetchesOnlyTheConnectionEndpoint()
    {
        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new RecordingJsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionId}}","name":"Live PostGIS","host":"db.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy","lastHealthCheck":"2026-05-24T10:00:00Z","storageType":"managed","sslMode":"Require"}]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var view = await dataSource.GetConnectionsViewAsync();

        Assert.Contains(view.Connections, connection => connection.Name == "Live PostGIS");
        // The connections surface must not block on services, layers, settings, or identity reads.
        Assert.Equal(["/api/v1/admin/connections/"], handler.RequestedPaths);
    }

    [Fact]
    public async Task SettingsViewFetchesOnlySettingsEndpoints()
    {
        var handler = new RecordingJsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/version"] = """
                {"success":true,"data":{"version":"1.2.3","metadataApiVersion":"v2","metadataSchemaVersion":"2026-05"}}
                """,
            ["/api/v1/admin/capabilities"] = """
                {"success":true,"data":{"metadataApiVersion":"v2","metadataSchemaVersion":"2026-05","serverVersion":"1.2.3"}}
                """,
            ["/api/v1/admin/license/"] = """
                {"success":true,"data":{"edition":"Community","isValid":true,"validationState":"valid","entitlements":[]}}
                """,
            ["/api/v1/admin/api-keys/"] = """
                {"success":true,"data":[]}
                """,
            ["/api/v1/admin/oidc/providers/"] = """
                {"success":true,"data":[]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var view = await dataSource.GetSettingsViewAsync();

        Assert.Contains(view.SettingsChanges, change => change.Id == "admin-version");
        // The settings surface reads only the control-plane endpoints, never connections/services/layers.
        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "/api/v1/admin/version",
                "/api/v1/admin/capabilities",
                "/api/v1/admin/license/",
                "/api/v1/admin/api-keys/",
                "/api/v1/admin/oidc/providers/"
            },
            handler.RequestedPaths.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LayersViewSkipsPerServiceSettingsReads()
    {
        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new RecordingJsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionId}}","name":"Live PostGIS","host":"db.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[{"serviceName":"planning","description":"Planning Service","layerCount":1,"enabledProtocols":["FeatureServer"]}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?serviceName=planning"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","geometryType":"Polygon","enabled":true,"serviceName":"planning"}]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var view = await dataSource.GetLayersViewAsync();

        Assert.Contains(view.Services, service => service.Layers.Any(layer => layer.Name == "Console Parcels"));
        // The layers surface renders the service/layer projection but does not need per-service settings.
        Assert.DoesNotContain(
            handler.RequestedPaths,
            path => path.Contains("/settings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishedLayersRenderWhenServicesEndpointReturnsNoRows()
    {
        // Regression: a connection can return published layers from the default scope even when the
        // admin services endpoint reports no rows (e.g. the Metadata v2 graph has not projected the
        // service yet). The layers must not disappear from the services and layers surfaces.
        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new RecordingJsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionId}}","name":"Live PostGIS","host":"db.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","geometryType":"Polygon","enabled":true,"serviceName":"planning"}]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var layersView = await dataSource.GetLayersViewAsync();
        var servicesView = await dataSource.GetServicesViewAsync();
        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.Contains(layersView.Services, service => service.Layers.Any(layer => layer.Name == "Console Parcels"));
        Assert.Contains(servicesView.Services, service => service.Layers.Any(layer => layer.Name == "Console Parcels"));
        Assert.Contains(workspace.Services, service => service.Layers.Any(layer => layer.Name == "Console Parcels"));
        // The service is derived from the layer's own service name, not the absent services endpoint.
        Assert.Contains(servicesView.Services, service => service.Name == "planning");
        // Resources read the same layer rows directly and must also keep the published layer.
        Assert.Contains(workspace.ResourceEdits, resource => resource.Name == "Console Parcels");
    }

    [Fact]
    public async Task DetailPagesRenderMissingItemAndCapabilityState()
    {
        var dataSource = new StubOperateTransitionDataSource(new OperateTransitionWorkspace(
            Connections: [],
            ResourceEdits: [],
            Services: [],
            SettingsChanges: [],
            CapabilityStates:
            [
                new OperateCapabilityState(
                    "Connections",
                    "Unavailable",
                    "GET /api/v1/admin/connections",
                    "The connections contract is not available."),
                new OperateCapabilityState(
                    "Resources",
                    "Unsupported",
                    "GET /api/v1/admin/metadata/resources",
                    "The resource edit contract is not available."),
                new OperateCapabilityState(
                    "Services",
                    "Unavailable",
                    "GET /api/v1/admin/services",
                    "The services contract is not available."),
                new OperateCapabilityState(
                    "Layers",
                    "Unavailable",
                    "GET /api/v1/admin/connections/{id}/layers",
                    "The layers contract is not available.")
            ]));

        var connectionHtml = await RenderPageAsync<OperateConnectionDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ConnectionId"] = "missing-connection"
            }));
        var html = await RenderPageAsync<OperateResourceDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ResourceId"] = "missing-resource"
            }));
        var serviceHtml = await RenderPageAsync<OperateServiceDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ServiceName"] = "missing-service"
            }));
        var layerHtml = await RenderPageAsync<OperateLayerDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ResourceId"] = "missing-resource"
            }));

        Assert.Contains("Connection Not Found", connectionHtml, StringComparison.Ordinal);
        Assert.Contains("Server Capability States", connectionHtml, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/admin/connections", connectionHtml, StringComparison.Ordinal);
        Assert.Contains("Resource Not Found", html, StringComparison.Ordinal);
        Assert.Contains("Server Capability States", html, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/admin/metadata/resources", html, StringComparison.Ordinal);
        Assert.Contains("Service Not Found", serviceHtml, StringComparison.Ordinal);
        Assert.Contains("Server Capability States", serviceHtml, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/admin/services", serviceHtml, StringComparison.Ordinal);
        Assert.Contains("Layer Not Found", layerHtml, StringComparison.Ordinal);
        Assert.Contains("Server Capability States", layerHtml, StringComparison.Ordinal);
        Assert.Contains("GET /api/v1/admin/connections/{id}/layers", layerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayerDetailRendersAllServiceExposuresForSharedLayerId()
    {
        // Regression: layer 7 is published by both the planning and public services. Both exposures
        // share one canonical resource id, the layers list links every exposure row to the same
        // /operate/layers/{canonicalResourceId} route, and the detail page must render every matching
        // service exposure, not an arbitrary first one.
        var connectionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new JsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionId}}","name":"Live PostGIS","host":"db.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[{"serviceName":"planning","description":"Planning Service","layerCount":1,"enabledProtocols":["FeatureServer"]},{"serviceName":"public","description":"Public Service","layerCount":1,"enabledProtocols":["FeatureServer"]}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?serviceName=planning"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","geometryType":"Polygon","enabled":true,"serviceName":"planning"}]}
                """,
            [$"/api/v1/admin/connections/{connectionId}/layers/?serviceName=public"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","geometryType":"Polygon","enabled":true,"serviceName":"public"}]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var layersView = await dataSource.GetLayersViewAsync();
        var resourceId = layersView.Services
            .SelectMany(service => service.Layers)
            .First(layer => layer.Name == "Console Parcels")
            .CanonicalResourceId;
        var layerHtml = await RenderPageAsync<OperateLayerDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ResourceId"] = resourceId
            }));

        // Both service exposures render, so either layers-list row lands on a page that shows every
        // service exposing layer 7 instead of silently collapsing to the first service.
        Assert.Contains("/operate/services/planning/settings", layerHtml, StringComparison.Ordinal);
        Assert.Contains("/operate/services/public/settings", layerHtml, StringComparison.Ordinal);
        Assert.Contains("Planning Service", layerHtml, StringComparison.Ordinal);
        Assert.Contains("Public Service", layerHtml, StringComparison.Ordinal);
        Assert.Contains("Console Parcels", layerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayerDetailDoesNotConflateSameLayerIdAcrossConnections()
    {
        // Regression: layer ids are only unique within a connection, so connection A and connection B
        // can both publish a layer 7 that point at different resources. Keying the detail route on the
        // bare integer conflated them; keying on the canonical resource id must render only the layer
        // belonging to the requested connection.
        var connectionA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var connectionB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new JsonFixtureHandler(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/api/v1/admin/connections/"] = $$"""
                {"success":true,"data":[{"connectionId":"{{connectionA}}","name":"Connection A","host":"a.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy"},{"connectionId":"{{connectionB}}","name":"Connection B","host":"b.internal","port":5432,"databaseName":"honua","username":"operator","provider":"PostgreSQL/PostGIS","isActive":true,"healthStatus":"Healthy"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[{"serviceName":"planning","description":"Planning Service","layerCount":2,"enabledProtocols":["FeatureServer"]}]}
                """,
            [$"/api/v1/admin/connections/{connectionA}/layers/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionB}/layers/"] = """
                {"success":true,"data":[]}
                """,
            [$"/api/v1/admin/connections/{connectionA}/layers/?serviceName=planning"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Connection A Parcels","schema":"public","table":"parcels","geometryType":"Polygon","enabled":true,"serviceName":"planning"}]}
                """,
            [$"/api/v1/admin/connections/{connectionB}/layers/?serviceName=planning"] = """
                {"success":true,"data":[{"layerId":7,"layerName":"Connection B Roads","schema":"public","table":"roads","geometryType":"Line","enabled":true,"serviceName":"planning"}]}
                """
        });
        var dataSource = CreateServerDataSource(handler);

        var layersView = await dataSource.GetLayersViewAsync();
        var connectionAResourceId = layersView.Services
            .SelectMany(service => service.Layers)
            .First(layer => layer.Name == "Connection A Parcels")
            .CanonicalResourceId;
        var connectionBResourceId = layersView.Services
            .SelectMany(service => service.Layers)
            .First(layer => layer.Name == "Connection B Roads")
            .CanonicalResourceId;
        var layerHtml = await RenderPageAsync<OperateLayerDetailPage>(
            dataSource,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["ResourceId"] = connectionAResourceId
            }));

        // Both connections expose a layer 7, but the two layers resolve to distinct resource ids.
        Assert.NotEqual(connectionAResourceId, connectionBResourceId);
        // The page shows only the requested connection's layer, never the same-numbered layer next door.
        Assert.Contains("Connection A Parcels", layerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection B Roads", layerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactorRemovesCredentialsTokensConnectionStringsAndSecretValues()
    {
        const string raw = """
            connectionString=Server=db.internal;Database=land;User Id=svc;Password=hunter2;
            api_key=abc123
            Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature
            secret://connections/prod-postgres/password-value-from-vault
            """;

        var redacted = OperateSecretRedactor.Redact(raw);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=db.internal", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("password-value-from-vault", redacted, StringComparison.Ordinal);
        Assert.Contains("connectionString=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("api_key=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedConnectionDiagnosticIsStructuredActionableAndSafe()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var connection = await dataSource.FindConnectionAsync("prod-postgres");

        var diagnostic = connection?.LastDiagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal("AUTH_INVALID_CREDENTIAL", diagnostic.FailureCode);
        Assert.NotEmpty(diagnostic.Signals);
        Assert.NotEmpty(diagnostic.OperatorActions);
        Assert.NotEmpty(diagnostic.Evidence);

        var renderedText = string.Join(
            " ",
            new[]
            {
                diagnostic.Summary,
                string.Join(" ", diagnostic.Signals.Select(signal => signal.Message)),
                string.Join(" ", diagnostic.OperatorActions),
                string.Join(" ", diagnostic.Evidence.Select(entry => $"{entry.Key}={entry.Value}"))
            });

        Assert.DoesNotContain("wrong-value", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=pg-prod.internal.honua", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-material", renderedText, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", renderedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataSourceReturnsOnlySafeDiagnostics()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();

        var workspace = await dataSource.GetWorkspaceAsync();
        var connection = await dataSource.FindConnectionAsync("prod-postgres");

        Assert.NotNull(connection);
        Assert.Same(connection, workspace.Connections.Single(candidate => candidate.Id == "prod-postgres"));
        AssertDiagnosticIsSafe(connection.LastDiagnostic);
    }

    [Fact]
    public async Task ResourceEditCarriesValidationStateAndBlastRadius()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var resource = await dataSource.FindResourceEditAsync("res-parcels-2026");

        Assert.NotNull(resource);
        Assert.Contains("Blocked", resource.ValidationState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(resource.ValidationIssues, issue => issue.Severity == "Error");
        Assert.NotEmpty(resource.BlastRadius.CatalogItems);
        Assert.NotEmpty(resource.BlastRadius.Services);
        Assert.NotEmpty(resource.BlastRadius.Layers);
        Assert.NotEmpty(resource.BlastRadius.SavedMaps);
        Assert.NotEmpty(resource.BlastRadius.ShareLinks);
        Assert.NotEmpty(resource.BlastRadius.GeneratedApps);
        Assert.Contains("Validation", resource.EditTabs);
        Assert.Contains("Advanced", resource.EditTabs);
    }

    [Fact]
    public async Task ServicesExposeLayersButPointMetadataToCanonicalResources()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var service = await dataSource.FindServiceAsync("planning-feature-service");

        Assert.NotNull(service);
        Assert.Contains("owned by data resources", service.MetadataOwnership, StringComparison.OrdinalIgnoreCase);
        Assert.All(service.Layers, layer =>
        {
            Assert.NotEqual(0, layer.LayerId);
            Assert.False(string.IsNullOrWhiteSpace(layer.CanonicalResourceId));
            Assert.False(string.IsNullOrWhiteSpace(layer.CanonicalResourceName));
        });
    }

    [Fact]
    public async Task SettingsChangesAlwaysShowApplyScopeAndRestartRequirement()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.All(workspace.SettingsChanges, change =>
        {
            Assert.False(string.IsNullOrWhiteSpace(change.ApplyScope));
            Assert.False(string.IsNullOrWhiteSpace(change.RestartRequirement));
            Assert.False(string.IsNullOrWhiteSpace(change.PolicyState));
        });
        Assert.Contains(workspace.SettingsChanges, change => change.Id == "cors" && change.RequiresRestart);
    }

    private static void AssertDiagnosticIsSafe(OperateConnectionDiagnostic? diagnostic)
    {
        Assert.NotNull(diagnostic);

        var renderedText = string.Join(
            " ",
            new[]
            {
                diagnostic.Summary,
                string.Join(" ", diagnostic.Signals.Select(signal => signal.Message)),
                string.Join(" ", diagnostic.OperatorActions),
                string.Join(" ", diagnostic.Evidence.Select(entry => $"{entry.Key}={entry.Value}"))
            });

        Assert.DoesNotContain("wrong-value", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=pg-prod.internal.honua", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-material", renderedText, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", renderedText, StringComparison.Ordinal);
    }

    private static async Task<string> RenderPageAsync<TComponent>(
        IOperateTransitionDataSource dataSource,
        ParameterView parameters)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(dataSource);
        // The connection detail page depends on the connection operation (for its Test button); the
        // missing-binding impl is the right no-network stand-in for these render tests.
        services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        // The service detail page depends on the service-configuration operation (protocol/access editor);
        // the missing-binding impl is the right no-network stand-in for these render tests.
        services.AddSingleton<IServiceConfigurationOperation, UnsupportedServiceConfigurationOperation>();
        // The layer detail page depends on the layer-fields operation; the missing-binding impl is the right
        // no-network stand-in for these render tests.
        services.AddSingleton<IConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>();
        // The layer detail page renders MapPreview, which [Inject]s IJSRuntime (never invoked under static
        // HtmlRenderer); a no-op satisfies DI.
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(new NoOpJsRuntime());
        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        await using var renderer = new HtmlRenderer(serviceProvider, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(parameters);
            return output.ToHtmlString();
        }).ConfigureAwait(false);
    }

    private sealed class StubOperateTransitionDataSource : IOperateTransitionDataSource
    {
        private readonly OperateTransitionWorkspace _workspace;

        public StubOperateTransitionDataSource(OperateTransitionWorkspace workspace)
        {
            _workspace = workspace;
        }

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace);

        public Task<OperateConnectionSummary?> FindConnectionAsync(
            string connectionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Connections.FirstOrDefault(
                connection => string.Equals(connection.Id, connectionId, StringComparison.OrdinalIgnoreCase)));

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.ResourceEdits.FirstOrDefault(
                resource => string.Equals(resource.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)));

        public Task<OperateServiceDetail?> FindServiceAsync(
            string serviceName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Services.FirstOrDefault(
                service => string.Equals(service.Name, serviceName, StringComparison.OrdinalIgnoreCase)));
    }

    private static HonuaServerOperateTransitionDataSource CreateServerDataSource(HttpMessageHandler handler)
    {
        var baseUri = new Uri("https://server.example");
        var httpClient = new HttpClient(handler) { BaseAddress = baseUri };
        var client = new HonuaAdminOperateHttpClient(
            httpClient,
            new HonuaAdminOperateClientOptions(baseUri, "test-api-key"));
        return new HonuaServerOperateTransitionDataSource(client);
    }

    private sealed class RecordingJsonFixtureHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _responses;
        private readonly List<string> _requestedPaths = new();
        private readonly object _gate = new();

        public RecordingJsonFixtureHandler(IReadOnlyDictionary<string, string> responses)
        {
            _responses = responses;
        }

        public IReadOnlyList<string> RequestedPaths
        {
            get
            {
                lock (_gate)
                {
                    return _requestedPaths.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.PathAndQuery ?? string.Empty;
            lock (_gate)
            {
                _requestedPaths.Add(key);
            }

            if (_responses.TryGetValue(key, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"success":false,"message":"missing fixture"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class JsonFixtureHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _responses;

        public JsonFixtureHandler(IReadOnlyDictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (_responses.TryGetValue(key, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"success":false,"message":"missing fixture"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }

    // MapPreview (rendered on the layer detail page) [Inject]s IJSRuntime. Static HtmlRenderer never runs
    // OnAfterRender, so JS is never actually invoked during these render tests — this no-op only satisfies DI.
    private sealed class NoOpJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args) => default;
    }
}
