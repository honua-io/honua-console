using System.Net;
using Honua.Console.Contracts;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

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
                {"success":true,"data":[{"layerId":7,"layerName":"Console Parcels","schema":"public","table":"parcels","description":"Fixture layer","geometryType":"Polygon","fieldCount":5,"enabled":true,"serviceName":"planning"}]}
                """,
            ["/api/v1/admin/services/"] = """
                {"success":true,"data":[{"serviceName":"planning","description":"Planning Service","layerCount":1,"enabledProtocols":["FeatureServer","MapServer"]}]}
                """,
            ["/api/v1/admin/services/planning/settings"] = """
                {"success":true,"data":{"serviceName":"planning","enabledProtocols":["FeatureServer"],"availableProtocols":["FeatureServer","MapServer"],"accessPolicy":{"allowAnonymous":true,"allowAnonymousWrite":false,"allowedRoles":["operator"],"allowedWriteRoles":[]},"mapServer":{"maxImageWidth":4096,"maxImageHeight":4096,"defaultFormat":"png","maxFeaturesPerLayer":1000}}}
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
        Assert.Contains(workspace.ResourceEdits, resource => resource.Name == "Console Parcels" && resource.ValidationState == "Published");
        Assert.Contains(workspace.Services, service => service.Name == "planning" && service.Layers.Any(layer => layer.Name == "Console Parcels"));
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
}
