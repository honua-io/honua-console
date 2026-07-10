using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the layer fields alias/hidden authoring gap (metadata-ui-gap Bucket 3-A #8).
/// Drives the real <see cref="OperateLayerDetailPage"/> through fakes (never a mock server) to prove:
///   (1) editing a field's alias + hidden and clicking Save issues the field-configuration update with
///       Alias/Hidden populated, and
///   (2) the page re-reads fields from the server afterwards so the table reflects the new state.
/// A second test pins the REAL <see cref="HonuaServerConsoleLayerFieldsOperation"/> so the
/// <c>PUT /api/v1/admin/metadata/layers/{id}/fields</c> request body actually carries Alias/Hidden.
/// </summary>
public sealed class OperateLayerFieldAliasHiddenTests
{
    private const string ResourceId = "conn-1-layer-7";
    private const int LayerId = 7;

    [Fact]
    public void Save_SetsAliasAndHidden_IssuesUpdateAndReReadsFields()
    {
        var fields = new RecordingLayerFieldsOperation(new ConsoleLayerFields
        {
            Bound = true,
            LayerId = LayerId,
            Fields =
            [
                new ConsoleLayerField { Name = "status", Type = "esriFieldTypeString", Alias = null, Hidden = false },
            ],
        });

        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        // The page hosts a <MapPreview/> that imports a JS module on render; the fields panel under test
        // needs no JS, so let unmatched interop calls no-op instead of failing the render.
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new FakeLayersDataSource(ResourceId, LayerId));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(fields);

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, ResourceId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-field-row]")),
            TimeSpan.FromSeconds(5));

        // Author a new alias + hide the field, then Save.
        page.Find("[data-field-alias]").Change("Status code");
        page.Find("[data-field-hidden]").Change(true);
        page.Find("[data-field-save]").Click();

        // (1) The Save issued exactly one configuration update with Alias/Hidden populated.
        page.WaitForAssertion(
            () => Assert.Single(fields.ConfigurationUpdates),
            TimeSpan.FromSeconds(5));
        var update = fields.ConfigurationUpdates[0];
        Assert.Equal(LayerId, update.LayerId);
        Assert.Equal("status", update.FieldName);
        Assert.Equal("Status code", update.Alias);
        Assert.True(update.Hidden);

        // (2) The page re-read fields after the successful save (round-trip).
        Assert.Equal(2, fields.GetCount);

        // The re-read reflects the new alias/hidden state in the fields table.
        page.WaitForAssertion(
            () => Assert.Contains("Status code", page.Find("[data-field-alias]").GetAttribute("value")!, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(page.Find("[data-field-hidden]").HasAttribute("checked"));
        Assert.Contains("Updated", page.Find("[data-field-result]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealOperation_SetFieldConfiguration_PutsAliasAndHidden()
    {
        var client = new RecordingAdminClient();
        var operation = new HonuaServerConsoleLayerFieldsOperation(client);

        var result = await operation.SetFieldConfigurationAsync(LayerId, "status", "Status code", hidden: true);

        Assert.True(result.Succeeded);
        Assert.NotNull(client.LastUpdate);
        var field = Assert.Single(client.LastUpdate!.Fields);
        Assert.Equal("status", field.Name);
        Assert.Equal("Status code", field.Alias);
        Assert.Equal(true, field.Hidden);
        // Alias/hidden update must not disturb the field's domain.
        Assert.Null(field.Domain);
    }

    /// <summary>Records the page's calls into <see cref="IConsoleLayerFieldsOperation"/> and serves the
    /// updated field state on re-read so the round-trip is observable.</summary>
    private sealed class RecordingLayerFieldsOperation : IConsoleLayerFieldsOperation
    {
        private ConsoleLayerFields _state;

        public RecordingLayerFieldsOperation(ConsoleLayerFields initial) => _state = initial;

        public int GetCount { get; private set; }

        public List<(int LayerId, string FieldName, string? Alias, bool Hidden)> ConfigurationUpdates { get; } = [];

        public Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(_state);
        }

        public Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(
            int layerId, string fieldName, string domainName,
            IReadOnlyList<ConsoleCodedValue> codedValues, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated" });

        public Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(
            int layerId, string fieldName, string? alias, bool hidden, CancellationToken cancellationToken = default)
        {
            ConfigurationUpdates.Add((layerId, fieldName, alias, hidden));
            // Persist the change so the page's re-read reflects it (server round-trip).
            _state = _state with
            {
                Fields = _state.Fields
                    .Select(f => f.Name == fieldName ? f with { Alias = alias, Hidden = hidden } : f)
                    .ToArray(),
            };
            return Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated", Detail = "Saved." });
        }
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
                    .Select(f => new HonuaAdminLayerField { Name = f.Name, Alias = f.Alias, Hidden = f.Hidden ?? false })
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
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetLayerDiscoveryAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateLayerDiscoveryAsync(int layerId, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> GetServiceDiscoveryAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminDiscoveryMetadata>> UpdateServiceDiscoveryAsync(string serviceName, HonuaAdminDiscoveryMetadataUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> GetLayerRelationshipsAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerRelationships>> UpdateLayerRelationshipsAsync(int layerId, HonuaAdminLayerRelationshipsUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> GetLayerDisplayAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerDisplay>> UpdateLayerDisplayAsync(int layerId, HonuaAdminLayerDisplayUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> GetLayerEditingAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerEditing>> UpdateLayerEditingAsync(int layerId, HonuaAdminLayerEditingUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> GetLayerSpatialAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerSpatial>> UpdateLayerSpatialAsync(int layerId, HonuaAdminLayerSpatialUpdate request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerPopupInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerPopupInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> GetLayerDrawingInfoAsync(int layerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HonuaAdminEndpointResult<HonuaAdminLayerAuthoringDocument>> UpdateLayerDrawingInfoAsync(int layerId, System.Text.Json.JsonElement? document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    /// <summary>Serves a single layer exposure so the page renders its fields panel; no fabricated content
    /// beyond the one layer under test.</summary>
    private sealed class FakeLayersDataSource : IOperateTransitionDataSource
    {
        private readonly OperateServicesView _view;

        public FakeLayersDataSource(string resourceId, int layerId)
        {
            var layer = new OperateServiceLayerProjection(
                layerId, "parcels", "Polygon", resourceId, "conn-1.parcels");
            var service = new OperateServiceDetail(
                "parcels-fs", "Parcels FeatureServer", "FeatureServer", "Running", "honua-server",
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
