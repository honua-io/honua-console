using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the publication-overrides authoring slice (gapc/pub-overrides): the
/// <see cref="HonuaServerConsolePublicationOverridesOperation"/> over a stubbed admin client, the
/// missing-binding <see cref="UnsupportedConsolePublicationOverridesOperation"/>, and the
/// <see cref="OperatePublicationOverridesPage"/> render/save round-trip. Asserts the real route/verb/body each
/// read+write issues for the publication overrides endpoint
/// (<c>GET/PUT /api/v1/admin/metadata/publications/{publicationId}/overrides</c>) — including the fieldAliases
/// map and the capabilities/supportedFormats arrays plus the isPrimary flag — the result mapping, and that the
/// unconfigured surface never performs a network call. No mocks of overrides data: every assertion is over the
/// wire the operation actually sends, or what a recorded server response maps to.
/// </summary>
public sealed class PublicationOverridesTests
{
    private const string PublicationId = "pub-123";
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task GetOverrides_IssuesGetToOverridesRoute_AndMapsFields()
    {
        string? path = null;
        HttpMethod? method = null;
        var operation = new HonuaServerConsolePublicationOverridesOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(SampleOverrides());
        }));

        var result = await operation.GetOverridesAsync(PublicationId);

        Assert.True(result.Bound);
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/api/v1/admin/metadata/publications/pub-123/overrides", path);
        Assert.Equal("Public parcels", result.TitleOverride);
        Assert.True(result.IsPrimary);
        Assert.Equal(new[] { "Query", "Create" }, result.Capabilities);
        Assert.Equal(new[] { "json", "geojson" }, result.SupportedFormats);
        var alias = Assert.Single(result.FieldAliases);
        Assert.Equal("OBJECTID", alias.Field);
        Assert.Equal("Record ID", alias.Alias);
    }

    [Fact]
    public async Task SaveOverrides_IssuesPutWithAliasMapAndArrays_ToOverridesRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var operation = new HonuaServerConsolePublicationOverridesOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(SampleOverrides());
        }));

        var result = await operation.SaveOverridesAsync(PublicationId, new ConsolePublicationOverrides
        {
            Bound = true,
            TitleOverride = "Public parcels",
            IsPrimary = true,
            Capabilities = new[] { "Query", "Create" },
            SupportedFormats = new[] { "json", "geojson" },
            FieldAliases = new[]
            {
                new ConsolePublicationFieldAlias { Field = "OBJECTID", Alias = "Record ID" },
                new ConsolePublicationFieldAlias { Field = "GEOM", Alias = "Shape" },
            },
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/metadata/publications/pub-123/overrides", path);

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("Public parcels", root.GetProperty("titleOverride").GetString());
        Assert.True(root.GetProperty("isPrimary").GetBoolean());

        var aliases = root.GetProperty("fieldAliases");
        Assert.Equal(JsonValueKind.Object, aliases.ValueKind);
        Assert.Equal("Record ID", aliases.GetProperty("OBJECTID").GetString());
        Assert.Equal("Shape", aliases.GetProperty("GEOM").GetString());

        var capabilities = root.GetProperty("capabilities");
        Assert.Equal(2, capabilities.GetArrayLength());
        Assert.Equal("Query", capabilities[0].GetString());

        var formats = root.GetProperty("supportedFormats");
        Assert.Equal(2, formats.GetArrayLength());
        Assert.Equal("json", formats[0].GetString());
    }

    [Fact]
    public async Task SaveOverrides_WithBlankTitleAndNoRows_ClearsTitleAndSendsEmptyMapAndArrays()
    {
        string? body = null;
        var operation = new HonuaServerConsolePublicationOverridesOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new HonuaAdminPublicationOverrides { PublicationId = PublicationId });
        }));

        var result = await operation.SaveOverridesAsync(PublicationId, new ConsolePublicationOverrides
        {
            Bound = true,
            TitleOverride = string.Empty,
            Capabilities = Array.Empty<string>(),
            SupportedFormats = Array.Empty<string>(),
            FieldAliases = Array.Empty<ConsolePublicationFieldAlias>(),
        });

        Assert.True(result.Succeeded);
        // Empty string clears the title; empty map/arrays clear those server-side.
        Assert.Contains("\"titleOverride\":\"\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"fieldAliases\":{}", body!, StringComparison.Ordinal);
        Assert.Contains("\"capabilities\":[]", body!, StringComparison.Ordinal);
        Assert.Contains("\"supportedFormats\":[]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetOverrides_WhenPublicationUnknown_MapsMissingBinding()
    {
        var operation = new HonuaServerConsolePublicationOverridesOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await operation.GetOverridesAsync("nope");

        Assert.False(result.Bound);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task SaveOverrides_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerConsolePublicationOverridesOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "capability 'Frobnicate' is not recognized." })
            }));

        var result = await operation.SaveOverridesAsync(PublicationId, new ConsolePublicationOverrides
        {
            Bound = true,
            Capabilities = new[] { "Frobnicate" },
        });

        Assert.False(result.Succeeded);
        Assert.Contains("not recognized", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedConsolePublicationOverridesOperation();

        var read = await operation.GetOverridesAsync(PublicationId);
        var write = await operation.SaveOverridesAsync(PublicationId, new ConsolePublicationOverrides { Bound = true });

        Assert.False(read.Bound);
        Assert.Contains("HONUA_SERVER_BASE_URL", read.Detail!, StringComparison.Ordinal);
        Assert.False(write.Succeeded);
        Assert.Equal("Missing binding", write.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", write.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefaultInterfaceMethods_ThrowNotSupported_ForFakeImplementors()
    {
        IHonuaAdminOperateClient fake = new MinimalAdminClient();

        await Assert.ThrowsAsync<NotSupportedException>(() => fake.GetPublicationOverridesAsync(PublicationId));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => fake.UpdatePublicationOverridesAsync(PublicationId, new HonuaAdminPublicationOverridesUpdate()));
    }

    [Fact]
    public void Page_WhenBound_RendersLoadedFieldsAndAliasRows()
    {
        var fake = new FakeOverrides
        {
            Read = new ConsolePublicationOverrides
            {
                Bound = true,
                PublicationId = PublicationId,
                TitleOverride = "Public parcels",
                IsPrimary = true,
                Capabilities = new[] { "Query", "Create" },
                SupportedFormats = new[] { "json" },
                FieldAliases = new[] { new ConsolePublicationFieldAlias { Field = "OBJECTID", Alias = "Record ID" } },
            },
        };
        var page = Render(fake, PublicationId);

        page.WaitForAssertion(
            () => Assert.Contains("data-overrides-title", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("value=\"Public parcels\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-overrides-alias-row", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-overrides-save", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_EditThenSave_IssuesSaveToPublication()
    {
        var fake = new FakeOverrides
        {
            Read = new ConsolePublicationOverrides { Bound = true, PublicationId = PublicationId },
            SaveResult = new ConsoleSavePublicationOverridesResult { Succeeded = true, State = "Updated", Detail = "Saved publication overrides on honua-server." },
        };
        var page = Render(fake, PublicationId);

        page.WaitForAssertion(
            () => Assert.Contains("data-overrides-title", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.Find("[data-overrides-title]").Change("New title");
        page.Find("[data-overrides-capabilities]").Change("Query, Update");
        page.Find("[data-overrides-formats]").Change("json, pbf");
        page.Find("[data-overrides-save]").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-overrides-result", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Equal(PublicationId, fake.SavedPublicationId);
        Assert.Equal("New title", fake.SavedOverrides?.TitleOverride);
        Assert.Equal(new[] { "Query", "Update" }, fake.SavedOverrides?.Capabilities);
        Assert.Equal(new[] { "json", "pbf" }, fake.SavedOverrides?.SupportedFormats);
        Assert.Contains("Saved publication overrides", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_WithoutPublicationId_RendersOperatorIdInput()
    {
        var fake = new FakeOverrides { Read = ConsolePublicationOverrides.Unbound("n/a") };
        var page = Render(fake, publicationId: null);

        page.WaitForAssertion(
            () => Assert.Contains("data-publication-id-input", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-publication-id-go", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_MergedBuildPage_RendersMissingBindingThroughRealDi()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsolePublicationOverridesOperation, UnsupportedConsolePublicationOverridesOperation>();

        var page = ctx.RenderComponent<OperatePublicationOverridesPage>(p => p.Add(x => x.PublicationId, PublicationId));

        page.WaitForAssertion(
            () => Assert.Contains("data-publication-overrides-unbound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("HONUA_SERVER_BASE_URL", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<OperatePublicationOverridesPage> Render(FakeOverrides fake, string? publicationId)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsolePublicationOverridesOperation>(fake);
        return ctx.RenderComponent<OperatePublicationOverridesPage>(p =>
        {
            if (publicationId is not null)
            {
                p.Add(x => x.PublicationId, publicationId);
            }
        });
    }

    private static HonuaAdminPublicationOverrides SampleOverrides() => new()
    {
        PublicationId = PublicationId,
        TitleOverride = "Public parcels",
        IsPrimary = true,
        Capabilities = new[] { "Query", "Create" },
        SupportedFormats = new[] { "json", "geojson" },
        FieldAliases = new Dictionary<string, string> { ["OBJECTID"] = "Record ID" },
    };

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }

    private sealed class FakeOverrides : IConsolePublicationOverridesOperation
    {
        public ConsolePublicationOverrides Read { get; set; } = ConsolePublicationOverrides.Unbound("test");
        public ConsoleSavePublicationOverridesResult? SaveResult { get; set; }
        public string? SavedPublicationId { get; private set; }
        public ConsolePublicationOverrides? SavedOverrides { get; private set; }

        public Task<ConsolePublicationOverrides> GetOverridesAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Read);

        public Task<ConsoleSavePublicationOverridesResult> SaveOverridesAsync(string publicationId, ConsolePublicationOverrides overrides, CancellationToken cancellationToken = default)
        {
            SavedPublicationId = publicationId;
            SavedOverrides = overrides;
            return Task.FromResult(SaveResult ?? new ConsoleSavePublicationOverridesResult { Succeeded = true, State = "Updated" });
        }
    }

    // A minimal IHonuaAdminOperateClient implementor that only provides BaseUri, to prove the new overrides
    // methods are DEFAULT INTERFACE METHODS that throw NotSupportedException for hand-rolled fakes (so the two
    // existing full test doubles keep compiling without edits).
    private sealed class MinimalAdminClient : IHonuaAdminOperateClient
    {
        public Uri BaseUri { get; } = BaseAddress;

        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileAsync(byte[] fileContent, string fileName, string tableName, string? targetSchema, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(string url, HonuaAdminExternalServiceCredentials? credentials = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(HonuaAdminGeoservicesImportRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(int layerId, HonuaAdminLayerFieldsUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> GetLayerDisplayAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> UpdateLayerDisplayAsync(int layerId, HonuaAdminLayerDisplayUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> GetLayerEditingAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> UpdateLayerEditingAsync(int layerId, HonuaAdminLayerEditingUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> GetLayerSpatialAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> UpdateLayerSpatialAsync(int layerId, HonuaAdminLayerSpatialUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(string connectionId, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(string connectionId, HonuaAdminPublishLayerRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(string connectionId, int layerId, bool enabled, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(string serviceName, IReadOnlyList<string> enabledProtocols, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(string serviceName, HonuaAdminUpdateAccessPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(string serviceName, HonuaAdminUpdateMapServerSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(string serviceName, HonuaAdminUpdateTimeInfoRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(int layerId, HonuaAdminLayerRelationshipsUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateLayerDiscoveryAsync(int layerId, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateServiceDiscoveryAsync(string serviceName, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<bool>> ProbeEndpointAsync(string contract, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(int layerId, JsonElement? document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(int layerId, JsonElement? document, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
