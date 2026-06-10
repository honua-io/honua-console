using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the extended layer-fields domain editor (range domain + per-field default value
/// + merge/split policy). Drives the real <see cref="OperateLayerDetailPage"/> through fakes (never a mock
/// server) to prove:
///   (1) authoring a RANGE domain with min/max + policies + a default value and clicking Save issues one
///       field-domain authoring update carrying the range/policies/default, and
///   (2) the page re-reads fields afterwards so the table reflects the new domain (round-trip).
/// A second pair of tests pins the REAL <see cref="HonuaServerConsoleLayerFieldsOperation"/> so the
/// <c>PUT /api/v1/admin/metadata/layers/{id}/fields</c> request body actually carries the range bounds, the
/// merge/split policy tokens, and the default value (and that the clear-default intent sends a JSON null).
/// </summary>
public sealed class OperateLayerFieldRangeDomainTests
{
    private const string ResourceId = "conn-1-layer-7";
    private const int LayerId = 7;

    [Fact]
    public void Save_AuthorsRangeDomainWithDefaultAndPolicies_IssuesUpdateAndReReadsFields()
    {
        var fields = new RecordingLayerFieldsOperation(new ConsoleLayerFields
        {
            Bound = true,
            LayerId = LayerId,
            Fields =
            [
                new ConsoleLayerField { Name = "elevation", Type = "esriFieldTypeDouble", Hidden = false },
            ],
        });

        using var ctx = new Bunit.BunitContext();
        // The page hosts a <MapPreview/> that imports a JS module on render; the fields panel under test needs
        // no JS, so let unmatched interop calls no-op instead of failing the render.
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeLayersDataSource(ResourceId, LayerId));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(fields);

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-domain-save]")),
            TimeSpan.FromSeconds(5));

        // Author a RANGE domain with min/max, merge/split policy, and a numeric default value.
        page.Find("[data-domain-field]").Change("elevation");
        page.Find("[data-domain-type]").Change(ConsoleDomainKind.Range.ToString());
        page.Find("[data-domain-name]").Change("elevation_range");
        page.Find("[data-domain-range-min]").Change("0");
        page.Find("[data-domain-range-max]").Change("8848");
        page.Find("[data-domain-merge-policy]").Change("esriMPTDefaultValue");
        page.Find("[data-domain-split-policy]").Change("esriSPTDuplicate");
        page.Find("[data-domain-default-value]").Change("0");
        page.Find("[data-domain-save]").Click();

        // (1) The Save issued exactly one domain authoring update carrying the range bounds, policies, default.
        page.WaitForAssertion(
            () => Assert.Single(fields.DomainUpdates),
            TimeSpan.FromSeconds(5));
        var authoring = fields.DomainUpdates[0].Authoring;
        Assert.Equal(LayerId, fields.DomainUpdates[0].LayerId);
        Assert.Equal("elevation", authoring.FieldName);
        Assert.Equal(ConsoleDomainKind.Range, authoring.Kind);
        Assert.Equal("elevation_range", authoring.DomainName);
        Assert.Equal(0, authoring.RangeMin);
        Assert.Equal(8848, authoring.RangeMax);
        Assert.Equal("esriMPTDefaultValue", authoring.MergePolicy);
        Assert.Equal("esriSPTDuplicate", authoring.SplitPolicy);
        Assert.Equal(ConsoleDefaultValueIntent.Set, authoring.DefaultValueIntent);
        Assert.Equal("0", authoring.DefaultValueText);

        // (2) The page re-read fields after the successful save (round-trip).
        Assert.Equal(2, fields.GetCount);

        // The re-read reflects the new range domain + default in the fields table.
        page.WaitForAssertion(
            () => Assert.Contains("range", page.Find("[data-field-domain]").TextContent, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5));
        Assert.Contains("default", page.Find("[data-field-default]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Updated", page.Find("[data-domain-result]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealOperation_SetDomain_PutsRangeDefaultAndPolicies()
    {
        var client = new RecordingAdminClient();
        var operation = new HonuaServerConsoleLayerFieldsOperation(client);

        var result = await operation.SetDomainAsync(LayerId, new ConsoleDomainAuthoring
        {
            FieldName = "elevation",
            DomainName = "elevation_range",
            Kind = ConsoleDomainKind.Range,
            RangeMin = 0,
            RangeMax = 8848,
            MergePolicy = "esriMPTDefaultValue",
            SplitPolicy = "esriSPTDuplicate",
            DefaultValueIntent = ConsoleDefaultValueIntent.Set,
            DefaultValueText = "0",
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(client.LastUpdate);
        var field = Assert.Single(client.LastUpdate!.Fields);
        Assert.Equal("elevation", field.Name);

        // Range domain with [min, max] bounds + policy tokens.
        Assert.NotNull(field.Domain);
        Assert.Equal("range", field.Domain!.Type);
        Assert.Equal("elevation_range", field.Domain.Name);
        Assert.NotNull(field.Domain.Range);
        Assert.Equal([0d, 8848d], field.Domain.Range!.ToArray());
        Assert.Equal("esriMPTDefaultValue", field.Domain.MergePolicy);
        Assert.Equal("esriSPTDuplicate", field.Domain.SplitPolicy);

        // Per-field default value carried as a JSON scalar (the typed "0" parses to a JSON number).
        Assert.NotNull(field.DefaultValue);
        Assert.Equal(JsonValueKind.Number, field.DefaultValue!.Value.ValueKind);
        Assert.Equal(0, field.DefaultValue.Value.GetDouble());
    }

    [Fact]
    public async Task RealOperation_ClearDefault_SendsJsonNull()
    {
        var client = new RecordingAdminClient();
        var operation = new HonuaServerConsoleLayerFieldsOperation(client);

        var result = await operation.SetDomainAsync(LayerId, new ConsoleDomainAuthoring
        {
            FieldName = "elevation",
            Kind = ConsoleDomainKind.None,
            DefaultValueIntent = ConsoleDefaultValueIntent.Clear,
        });

        Assert.True(result.Succeeded);
        var field = Assert.Single(client.LastUpdate!.Fields);
        // Clearing the default sends an explicit JSON null (not a C# null / omitted property).
        Assert.NotNull(field.DefaultValue);
        Assert.Equal(JsonValueKind.Null, field.DefaultValue!.Value.ValueKind);
        // No domain authored means the existing domain is left untouched (Domain stays null).
        Assert.Null(field.Domain);
    }

    [Fact]
    public async Task RealOperation_InvalidRange_RejectsWithoutPut()
    {
        var client = new RecordingAdminClient();
        var operation = new HonuaServerConsoleLayerFieldsOperation(client);

        var result = await operation.SetDomainAsync(LayerId, new ConsoleDomainAuthoring
        {
            FieldName = "elevation",
            Kind = ConsoleDomainKind.Range,
            RangeMin = 100,
            RangeMax = 10,
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid", result.State);
        // The invalid range never reached the server.
        Assert.Null(client.LastUpdate);
    }

    /// <summary>Records the page's domain-authoring calls and serves the updated field state on re-read so the
    /// round-trip is observable. The bUnit test only exercises SetDomainAsync + GetFieldsAsync.</summary>
    private sealed class RecordingLayerFieldsOperation : IConsoleLayerFieldsOperation
    {
        private ConsoleLayerFields _state;

        public RecordingLayerFieldsOperation(ConsoleLayerFields initial) => _state = initial;

        public int GetCount { get; private set; }

        public List<(int LayerId, ConsoleDomainAuthoring Authoring)> DomainUpdates { get; } = [];

        public Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(_state);
        }

        public Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
            int layerId, string fieldName, string domainName,
            IReadOnlyList<ConsoleCodedValue> codedValues, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated" });

        public Task<ConsoleSetDomainResult> SetDomainAsync(
            int layerId, ConsoleDomainAuthoring authoring, CancellationToken cancellationToken = default)
        {
            DomainUpdates.Add((layerId, authoring));
            // Persist the authored domain + default so the page's re-read reflects it (server round-trip).
            _state = _state with
            {
                Fields = _state.Fields
                    .Select(f => f.Name == authoring.FieldName
                        ? f with
                        {
                            DomainName = authoring.DomainName,
                            DomainKind = authoring.Kind,
                            RangeMin = authoring.RangeMin,
                            RangeMax = authoring.RangeMax,
                            MergePolicy = authoring.MergePolicy,
                            SplitPolicy = authoring.SplitPolicy,
                            DefaultValueText = authoring.DefaultValueIntent == ConsoleDefaultValueIntent.Set
                                ? authoring.DefaultValueText
                                : f.DefaultValueText,
                        }
                        : f)
                    .ToArray(),
            };
            return Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated", Detail = "Saved." });
        }

        public Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
            int layerId, string fieldName, string? alias, bool hidden, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated" });
    }

    /// <summary>Minimal recording admin client: captures the PUT body for the fields endpoint and echoes it
    /// back; every other member is unused by these tests.</summary>
    private sealed class RecordingAdminClient : IHonuaAdminOperateClient
    {
        public HonuaAdminLayerFieldsUpdate? LastUpdate { get; private set; }

        public Uri BaseUri => new("https://server.test");

        public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
            int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaAdminLayerFields>.FromData(
                new HonuaAdminLayerFields { LayerId = layerId, Fields = [] }));

        public Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
            int layerId, HonuaAdminLayerFieldsUpdate request, CancellationToken cancellationToken = default)
        {
            LastUpdate = request;
            var echoed = new HonuaAdminLayerFields
            {
                LayerId = layerId,
                Fields = request.Fields
                    .Select(f => new HonuaAdminLayerField
                    {
                        Name = f.Name,
                        Alias = f.Alias,
                        Hidden = f.Hidden ?? false,
                        Domain = f.Domain,
                        DefaultValue = f.DefaultValue,
                    })
                    .ToArray(),
            };
            return Task.FromResult(HonuaAdminEndpointResult<HonuaAdminLayerFields>.FromData(echoed));
        }

        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary[]>> ListConnectionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionSummary>> CreateConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestDraftConnectionAsync(HonuaAdminCreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminConnectionTestResult>> TestConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminTableInfo[]>> ListConnectionTablesAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminImportFormats>> GetImportFormatsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminImportResult>> ImportFileAsync(byte[] fileContent, string fileName, string tableName, string? targetSchema, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminExternalServiceDiscovery>> DiscoverExternalServiceAsync(string url, HonuaAdminExternalServiceCredentials? credentials = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportJob>> StartGeoservicesImportAsync(HonuaAdminGeoservicesImportRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminGeoservicesImportProgress>> GetGeoservicesImportJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary[]>> ListConnectionLayersAsync(string connectionId, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> PublishLayerAsync(string connectionId, HonuaAdminPublishLayerRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminPublishedLayerSummary>> SetLayerEnabledAsync(string connectionId, int layerId, bool enabled, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSummary[]>> ListServicesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> GetServiceSettingsAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceProtocolsAsync(string serviceName, IReadOnlyList<string> enabledProtocols, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceAccessPolicyAsync(string serviceName, HonuaAdminUpdateAccessPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminVersionResponse>> GetVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminCapabilitiesResponse>> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLicenseStatusResponse>> GetLicenseStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminApiKeyResponse[]>> ListApiKeysAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminOidcProviderResponse[]>> ListOidcProvidersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<bool>> ProbeEndpointAsync(string contract, string relativePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceMapServerSettingsAsync(string serviceName, HonuaAdminUpdateMapServerSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminServiceSettingsResponse>> UpdateServiceTimeInfoAsync(string serviceName, HonuaAdminUpdateTimeInfoRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(int layerId, HonuaAdminLayerRelationshipsUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> GetLayerDisplayAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> UpdateLayerDisplayAsync(int layerId, HonuaAdminLayerDisplayUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> GetLayerEditingAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> UpdateLayerEditingAsync(int layerId, HonuaAdminLayerEditingUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> GetLayerSpatialAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> UpdateLayerSpatialAsync(int layerId, HonuaAdminLayerSpatialUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateLayerDiscoveryAsync(int layerId, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateServiceDiscoveryAsync(string serviceName, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    /// <summary>Serves a single layer exposure so the page renders its fields panel; no fabricated content
    /// beyond the one layer under test.</summary>
    private sealed class FakeLayersDataSource : IOperateTransitionDataSource
    {
        private readonly OperateServicesView _view;

        public FakeLayersDataSource(string resourceId, int layerId)
        {
            var layer = new OperateServiceLayerProjection(
                layerId, "terrain", "Polygon", resourceId, "conn-1.terrain");
            var service = new OperateServiceDetail(
                "terrain-fs", "Terrain FeatureServer", "FeatureServer", "Running", "honua-server",
                [layer], [], []);
            _view = new OperateServicesView([service], []);
        }

        public Task<OperateServicesView> GetLayersViewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_view);

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new OperateTransitionWorkspace([], [], _view.Services, [], []));

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateConnectionSummary?>(null);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateServiceDetail?>(_view.Services.FirstOrDefault());
    }
}
