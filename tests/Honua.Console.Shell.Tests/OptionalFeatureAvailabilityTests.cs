using System.Net;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class OptionalFeatureAvailabilityTests
{
    private readonly InMemoryConsoleEnvironmentProfileStore _profiles = new([
        new ConsoleEnvironmentProfile { Id = "live", ServerBaseUri = new Uri("https://server.example") }
    ]);
    private readonly InMemoryConsoleAccountSessionStore _sessions = new();

    [Theory]
    [InlineData(HttpStatusCode.NotFound, SceneReadStatus.Unsupported)]
    [InlineData(HttpStatusCode.NotImplemented, SceneReadStatus.Unsupported)]
    [InlineData(HttpStatusCode.Forbidden, SceneReadStatus.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, SceneReadStatus.Unavailable)]
    public async Task SceneDiscoveryDistinguishesAbsentFeatureFromFailures(HttpStatusCode code, SceneReadStatus expected)
    {
        var result = await new HttpConsoleSceneClient(Http(code), _profiles, _sessions).ListScenesAsync();
        Assert.Equal(expected, result.Status);
        if (expected == SceneReadStatus.Unsupported) { Assert.Contains("scene.catalog", result.Message); }
    }

    [Fact]
    public async Task MissingSensorCollectionsAreOptionalButMissingDatastreamsRemainMissing()
    {
        var client = new HttpConsoleSensorThingsClient(Http(HttpStatusCode.NotFound), _profiles, _sessions);
        var things = await client.ListThingsAsync();
        Assert.Equal(SensorThingsReadStatus.Unsupported, things.Status);
        Assert.Contains("serve.sensorthings", things.Message);
        Assert.Equal(SensorThingsReadStatus.Unsupported, (await client.ListDatastreamsAsync()).Status);
        Assert.Equal(SensorThingsReadStatus.Missing, (await client.GetDatastreamAsync(12)).Status);
        Assert.Equal(SensorThingsReadStatus.Missing, (await client.ListDatastreamObservationsAsync(12)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, OperateSectionStatus.Unsupported)]
    [InlineData(HttpStatusCode.NotImplemented, OperateSectionStatus.Unsupported)]
    [InlineData(HttpStatusCode.Forbidden, OperateSectionStatus.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, OperateSectionStatus.Unavailable)]
    public async Task AlertListsDistinguishAbsentFeatureFromFailures(HttpStatusCode code, OperateSectionStatus expected)
    {
        var inbox = await new HttpConsoleOpsFindingsClient(Http(code), _profiles, _sessions).ListAlertsAsync();
        var activity = await new HttpConsoleOperateObservabilityClient(Http(code), _profiles, _sessions).GetAlertsAsync();
        Assert.Equal(expected, inbox.Status);
        Assert.Equal(expected, activity.Status);
        if (expected == OperateSectionStatus.Unsupported) { Assert.Contains("alerts.geofence", inbox.Message); }
    }

    [Theory]
    [InlineData(OperateSectionStatus.Unsupported, "informational")]
    [InlineData(OperateSectionStatus.Forbidden, "denied")]
    [InlineData(OperateSectionStatus.Unavailable, "denied")]
    [InlineData(OperateSectionStatus.Missing, "denied")]
    public void OnlyUnsupportedSectionsUseNeutralPresentation(OperateSectionStatus status, string expectedTreatment)
    {
        using var context = new BunitContext();
        var panel = context.Render<OperateSectionStatusPanel>(parameters => parameters
            .Add(p => p.Heading, "Optional feature").Add(p => p.Status, status));
        Assert.Single(panel.FindAll($".console-state-{expectedTreatment}"));
        Assert.Single(panel.FindAll(".console-state-informational, .console-state-denied"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task MissingCatalogRegistryIsNotConfiguredButIndividualEndpointsRemainMissing(HttpStatusCode code)
    {
        using var client = new HonuaCatalogDiscoveryHttpClient(Http(code),
            new HonuaCatalogDiscoveryClientOptions(new Uri("https://server.example")));
        var source = new HonuaServerCatalogDiscoveryDataSource(client);
        var registry = await source.LoadRegistryAsync("current");
        var state = Assert.Single(registry.CapabilityStates);
        Assert.Equal("Not configured", state.State);
        Assert.Contains("Console:CatalogDiscovery:Workspaces", state.Detail);
        Assert.DoesNotContain(((int)code).ToString(System.Globalization.CultureInfo.InvariantCulture), state.Detail);
        var endpoint = await source.LoadEndpointAsync("current", "ogc");
        Assert.NotEqual("Not configured", Assert.Single(endpoint.CapabilityStates).State);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CatalogRegistryFailuresKeepDiagnostics(HttpStatusCode code)
    {
        using var client = new HonuaCatalogDiscoveryHttpClient(Http(code),
            new HonuaCatalogDiscoveryClientOptions(new Uri("https://server.example")));
        var source = new HonuaServerCatalogDiscoveryDataSource(client);
        var registry = await source.LoadRegistryAsync("current");
        var state = Assert.Single(registry.CapabilityStates);
        Assert.NotEqual("Not configured", state.State);
        Assert.Contains($"HTTP {(int)code}", state.Detail);
        var endpoint = await source.LoadEndpointAsync("current", "ogc");
        Assert.NotEqual("Not configured", Assert.Single(endpoint.CapabilityStates).State);
    }

    private static HttpClient Http(HttpStatusCode status) => new(new StatusHandler(status));

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }
}
