using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerFieldAliasHiddenTests
{
    private const string ResourceId = "conn-1-layer-7";
    private const int LayerId = 7;

    [Theory]
    [InlineData(false, ResourceId, "/")]
    [InlineData(false, ResourceId, "/console/")]
    [InlineData(true, ResourceId, "/")]
    [InlineData(true, ResourceId, "/console/")]
    [InlineData(true, "7", "/console/")]
    [InlineData(true, "CONN-1-LAYER-7", "/console/")]
    public void Save_SetsAliasAndHidden_IssuesUpdateAndReReadsFields(bool enabled, string layerReference, string mount)
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
        ctx.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(new MountedNavigationManager(mount));
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        // The page hosts a <MapPreview/> that imports a JS module on render; the fields panel under test
        // needs no JS, so let unmatched interop calls no-op instead of failing the render.
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(LayerData(ResourceId, LayerId));
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation>(fields);

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, layerReference));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-field-row]")),
            TimeSpan.FromSeconds(5));

        Assert.Equal($"{mount}operate/layers/{ResourceId}/style", page.Find("[data-layer-style-link]").GetAttribute("href"));
        var registry = ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        if (enabled)
        {
            var entity = Assert.Single(registry.Current.Selection);
            Assert.Equal(ConsoleAgentEntityKind.Layer, entity.Kind);
            Assert.Equal(LayerId.ToString(), entity.Id);
            Assert.Equal(ResourceId, entity.ResourceId);
            Assert.Equal($"/operate/layers/{ResourceId}", ConsoleAgentRoutes.ForEntity(entity));
            Assert.Equal(ConsoleAgentRoutes.ForEntity(entity), registry.Current.Route);
            Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
            if (mount == "/console/") CatalogAdminPreview.Capture("data-layer-detail", page.Markup);
        }
        else Assert.Empty(registry.Current.Selection);

        // Author a new alias + hide the field, then Save.
        page.Find("[data-field-alias]").Change("Status code");
        page.Find("[data-field-hidden]").Change(true);
        if (enabled)
        {
            Assert.Equal(2, registry.Current.Unsaved.ChangedFieldCount);
            Assert.DoesNotContain("Status code", System.Text.Json.JsonSerializer.Serialize(registry.Current.Unsaved), StringComparison.Ordinal);
        }
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
        if (enabled)
        {
            page.WaitForAssertion(() => Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount));
            Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
            if (mount == "/console/") CatalogAdminPreview.Capture("data-layer-detail-alias-saved", page.Markup);
        }
    }

    [Fact]
    public async Task CON_001_AliasUpdate_PreservesExistingDomain()
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
        Assert.Same(client.CurrentDomain, field.Domain);
    }

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

    private sealed class RecordingAdminClient : UnusedAdminOperateClient
    {
        public HonuaAdminFieldDomain CurrentDomain { get; } = new()
        {
            Name = "status_domain",
            Type = "codedValue",
            CodedValues =
            [
                new HonuaAdminCodedValue
                {
                    Code = System.Text.Json.JsonSerializer.SerializeToElement(1),
                    Name = "Open",
                },
            ],
        };

        public HonuaAdminLayerFieldsUpdate? LastUpdate { get; private set; }

        public override Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> GetLayerFieldsAsync(
            int layerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(HonuaAdminEndpointResult<HonuaAdminLayerFields>.FromData(
                new HonuaAdminLayerFields
                {
                    LayerId = layerId,
                    Fields = [new HonuaAdminLayerField { Name = "status", Domain = CurrentDomain }],
                }));

        public override Task<HonuaAdminEndpointResult<HonuaAdminLayerFields>> UpdateLayerFieldsAsync(
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
    }

    private static FixedServicesDataSource LayerData(string resourceId, int layerId) =>
        new(new OperateServiceDetail(
            "parcels-fs", "Parcels FeatureServer", "FeatureServer", "Running", "honua-server",
            [new OperateServiceLayerProjection(layerId, "parcels", "Polygon", resourceId, "conn-1.parcels")],
            [],
            []));
}
