using System.Net;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Routes the pinned honua-server does not register fail closed with no HTTP, even when a server
/// base URL is configured (honua-console#420). Registered admin routes still send.
/// </summary>
public sealed class UnregisteredServerRouteTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public void Catalog_ExcludesRegisteredAdminAndStudioRoutes()
    {
        Assert.Equal(31, UnregisteredServerRoutes.All.Count);
        Assert.Contains("GET /api/v1/admin/services/{serviceName}/settings-caps", UnregisteredServerRoutes.All);
        Assert.Contains("POST /api/v1/console/workflow-packages/generate", UnregisteredServerRoutes.All);
        Assert.DoesNotContain("GET /api/v1/admin/metadata/layers/{layerId}/fields", UnregisteredServerRoutes.All);
        Assert.DoesNotContain("GET /api/v1/admin/metadata/layers/{layerId}/relationships", UnregisteredServerRoutes.All);
        Assert.DoesNotContain("PUT /api/v1/admin/services/{serviceName}/timeinfo", UnregisteredServerRoutes.All);
        Assert.DoesNotContain("POST /api/v1/studio/app-packages/generate", UnregisteredServerRoutes.All);
    }

    [Fact]
    public async Task RegisteredFieldRead_StillSendsHttp()
    {
        var calls = 0;
        string? path = null;
        using var client = new HonuaAdminOperateHttpClient(
            new HttpClient(new CountingHandler(_ =>
            {
                calls++;
                path = _.RequestUri?.AbsolutePath;
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }))
            { BaseAddress = BaseAddress },
            new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));

        var result = await client.GetLayerFieldsAsync(1);

        Assert.Equal(1, calls);
        Assert.Equal("/api/v1/admin/metadata/layers/1/fields", path);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.DoesNotContain(UnregisteredServerRoutes.Detail, result.Issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnregisteredOperateAndStudioRoutes_ReturnUnsupportedWithoutHttp()
    {
        var calls = 0;
        HttpMessageHandler Handler() => new CountingHandler(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var operate = new HonuaAdminOperateHttpClient(
            new HttpClient(Handler()) { BaseAddress = BaseAddress },
            new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
        using var forms = new HonuaFormPackageHttpClient(
            new HttpClient(Handler()) { BaseAddress = BaseAddress },
            new HonuaFormPackageClientOptions(BaseAddress, "test-key"));
        using var analysis = new HonuaAnalysisContentHttpClient(
            new HttpClient(Handler()) { BaseAddress = BaseAddress },
            new HonuaAnalysisContentClientOptions(BaseAddress, "test-key"));
        using var publications = new HonuaContentPublicationHttpClient(
            new HttpClient(Handler()) { BaseAddress = BaseAddress },
            new HonuaContentPublicationClientOptions(BaseAddress, "test-key"));
        using var workflows = new HttpWorkflowPackageApiClient(
            new HttpClient(Handler()) { BaseAddress = BaseAddress },
            new WorkflowPackageClientOptions(BaseAddress, "test-key"));

        var results = new List<HonuaAdminEndpointIssue?>
        {
            (await operate.GetServiceSettingsCapsAsync("svc")).Issue,
            (await operate.UpdateServiceSettingsCapsAsync("svc", new HonuaAdminUpdateServiceSettingsCapsRequest())).Issue,
            (await operate.GetLayerExtrusionAsync(1)).Issue,
            (await operate.UpdateLayerExtrusionAsync(1, new HonuaAdminLayerExtrusionUpdate())).Issue,
            (await operate.GetLayerStatusAsync(1)).Issue,
            (await operate.UpdateLayerStatusAsync(1, new HonuaAdminLayerStatusUpdate())).Issue,
            (await operate.GetPublicationOverridesAsync("pub")).Issue,
            (await operate.UpdatePublicationOverridesAsync("pub", new HonuaAdminPublicationOverridesUpdate())).Issue,
            (await operate.GetLayerSubtypesAsync(1)).Issue,
            (await operate.UpdateLayerSubtypesAsync(1, new HonuaAdminLayerSubtypesUpdate())).Issue,
            (await operate.GetLayerAttributeRulesAsync(1)).Issue,
            (await operate.UpdateLayerAttributeRulesAsync(1, new HonuaAdminLayerAttributeRulesUpdate())).Issue,
            (await operate.GetLayerDiscoveryAsync(1)).Issue,
            (await operate.UpdateLayerDiscoveryAsync(1, new HonuaAdminDiscoveryMetadataUpdate())).Issue,
            (await operate.GetServiceDiscoveryAsync("svc")).Issue,
            (await operate.UpdateServiceDiscoveryAsync("svc", new HonuaAdminDiscoveryMetadataUpdate())).Issue,
            (await operate.GetLayerDisplayAsync(1)).Issue,
            (await operate.UpdateLayerDisplayAsync(1, new HonuaAdminLayerDisplayUpdate())).Issue,
            (await operate.GetLayerEditingAsync(1)).Issue,
            (await operate.UpdateLayerEditingAsync(1, new HonuaAdminLayerEditingUpdate())).Issue,
            (await operate.GetLayerSpatialAsync(1)).Issue,
            (await operate.UpdateLayerSpatialAsync(1, new HonuaAdminLayerSpatialUpdate())).Issue,
            (await operate.ProbeEndpointAsync(
                "GET /api/v1/admin/metadata/layers/{layerId}/display",
                "/api/v1/admin/metadata/layers/1/display")).Issue,
            (await forms.ListGenerationProvidersAsync()).Issue,
            (await forms.GenerateFormAsync(new GenerateFormPackageRequest { Prompt = "a form" })).Issue,
            (await forms.ImportXlsFormAsync(new ImportXlsFormRequest { FileName = "f.xlsx", Content = "AQ==" })).Issue,
            (await analysis.GenerateAsync(new HonuaGenerateAnalysisRequest { Prompt = "hotspot" })).Issue,
            (await analysis.GenerateQueryAsync(new HonuaGenerateSavedQueryRequest { Prompt = "parcels" })).Issue,
            (await publications.ListReportGenerationProvidersAsync()).Issue,
            (await publications.GenerateReportAsync(new GenerateReportContentRequest { Prompt = "report" })).Issue,
            (await publications.GenerateDashboardAsync(new GenerateDashboardContentRequest { Prompt = "board" })).Issue,
        };

        var providers = await workflows.ListGenerationProvidersAsync();
        var generated = await workflows.GenerateWorkflowAsync(new GenerateWorkflowRequest { Prompt = "buffer" });

        Assert.Equal(0, calls);
        Assert.Equal(31, results.Count);
        foreach (var issue in results)
        {
            Assert.NotNull(issue);
            Assert.Equal(UnregisteredServerRoutes.State, issue!.State);
            Assert.Equal(UnregisteredServerRoutes.StatusCode, issue.StatusCode);
            Assert.Equal(UnregisteredServerRoutes.Detail, issue.Detail);
        }

        Assert.Equal(UnregisteredServerRoutes.Detail, providers.Issue!.Detail);
        Assert.Equal(UnregisteredServerRoutes.Detail, generated.Issue!.Detail);
        var seen = results
            .Select(issue => issue!.Contract)
            .Append(providers.Issue.Contract)
            .Append(generated.Issue.Contract)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(UnregisteredServerRoutes.All.SetEquals(seen));
    }

    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
