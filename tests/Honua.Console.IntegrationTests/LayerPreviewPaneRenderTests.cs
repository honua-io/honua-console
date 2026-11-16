using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class LayerPreviewPaneRenderTests
{
    private static readonly LayerPreviewPane.PreviewSelection Selection =
        new("conn-1-layer-7", "Parcels", "featureserver", "FeatureServer", "/rest/services/cadastre/FeatureServer/7");

    private static readonly OperateServiceLayer Parcels =
        new(7, "Parcels", "Polygon", "conn-1-layer-7", "parcels", [-158.3, 21.2, -157.6, 21.7]) { ServiceName = "cadastre" };

    [Fact]
    public void SelectedPublication_MountsALiveMapOnTheProxiedLayerStyle()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient());
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p
            .Add(x => x.Selection, Selection)
            .Add(x => x.Layer, Parcels)
            .Add(x => x.EnvironmentId, "env-1"));

        pane.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        Assert.Contains("/map-proxy/env-1/styles/pub-cadastre-parcels.json", System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]), StringComparison.Ordinal);
        Assert.Equal("Map preview of layer Parcels", pane.Find("figure.map-preview").GetAttribute("aria-label"));
    }

    private static HonuaAdminEndpointResult<DiscoveryMetadataResponse> Publications(params DiscoveryPublicationImpact[] publications) =>
        HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromData(new()
        {
            Etag = "test",
            EntityId = "resource-parcels",
            AffectedPublications = publications
        });

    [Theory]
    [InlineData(0, "normal-first")]
    [InlineData(0, "stac-first")]
    [InlineData(7, "normal-first")]
    [InlineData(7, "stac-first")]
    public void EquivalentPublications_ResolveTheSelectedStorageLayerDeterministically(int layerId, string order)
    {
        using var ctx = new BunitContext();
        var normal = new DiscoveryPublicationImpact("pub-normal", "CADASTRE", "37") { PreviewStorageLayerId = layerId };
        var stac = new DiscoveryPublicationImpact("pub-stac", "cadastre", "catalog-item") { PreviewStorageLayerId = layerId };
        var other = new DiscoveryPublicationImpact("pub-a-other-binding", "cadastre", "37") { PreviewStorageLayerId = layerId + 1 };
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Result = order == "normal-first" ? Publications(normal, stac, other) : Publications(other, stac, normal)
        });
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels with { LayerId = layerId }).Add(x => x.EnvironmentId, "env-1"));

        preview.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        Assert.Contains("/styles/pub-normal.json", System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mismatched")]
    [InlineData("unknown-alongside-matching")]
    [InlineData("negative")]
    public void UnprovedStorageIdentity_DoesNotMountAMap(string identity)
    {
        using var ctx = new BunitContext();
        var matching = new DiscoveryPublicationImpact("pub-matching", "cadastre", "7") { PreviewStorageLayerId = 7 };
        var unproved = new DiscoveryPublicationImpact("pub-unproved", "cadastre", "7")
        {
            PreviewStorageLayerId = identity == "mismatched" ? 8 : identity == "negative" ? -1 : null
        };
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Result = identity == "unknown-alongside-matching" ? Publications(matching, unproved) : Publications(unproved)
        });
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));

        Assert.NotNull(preview.Find("[data-preview-unrendered]"));
        Assert.Empty(preview.FindAll("figure.map-preview"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrAmbiguousPublication_DoesNotFallBackToStorageId(bool ambiguous)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Result = ambiguous
                ? Publications(new DiscoveryPublicationImpact("pub-a", "cadastre", "a"), new("pub-b", "cadastre", "b"))
                : Publications(new DiscoveryPublicationImpact("pub-other", "another-service", "parcels"))
        });
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        Assert.NotNull(preview.Find("[data-preview-unrendered]"));
        Assert.Empty(preview.FindAll("figure.map-preview"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePublicationResponse_CannotReplaceNewSelection(bool failedRead)
    {
        using var ctx = new BunitContext();
        var pending = new TaskCompletionSource<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>();
        var reads = 0;
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Read = () => ++reads == 1 ? pending.Task : Task.FromResult(Publications(new DiscoveryPublicationImpact("pub-new", "cadastre", "new")))
        });
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;
        module.Setup<bool>("init", _ => true).SetResult(true);
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        preview.Render(p => p.Add(x => x.Layer, Parcels with { LayerId = 8 }));
        preview.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        await preview.InvokeAsync(() => pending.SetResult(failedRead
            ? HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromIssue(new("Forbidden", "discovery", "Denied", 403))
            : Publications(new DiscoveryPublicationImpact("pub-old", "cadastre", "old"))));
        Assert.Single(module.Invocations["init"]);
        Assert.Contains("/styles/pub-new.json", System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]), StringComparison.Ordinal);
        Assert.DoesNotContain("pub-old", preview.Markup, StringComparison.Ordinal);
        Assert.Empty(preview.FindComponents<OperateSectionStatusPanel>());
    }

    [Fact]
    public async Task EnvironmentSwitch_ClearsTheResolvedPreview()
    {
        using var ctx = new BunitContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new() { Id = "env-1" }, new() { Id = "env-2" }
        ]);
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient());
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        preview.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));

        await preview.InvokeAsync(() => profiles.ActivateProfileAsync("env-2"));

        preview.WaitForAssertion(() => Assert.Empty(preview.FindAll("figure.map-preview")));
        Assert.NotNull(preview.Find("[data-preview-unrendered]"));
    }

    [Theory]
    [InlineData(403, OperateSectionStatus.Forbidden)]
    [InlineData(503, OperateSectionStatus.Unavailable)]
    public void FailedMetadata_ShowsSharedStatusWithoutMountingAMap(int statusCode, OperateSectionStatus expectedStatus)
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Result = HonuaAdminEndpointResult<DiscoveryMetadataResponse>.FromIssue(new(statusCode == 403 ? "Forbidden" : "Unavailable", "discovery", "Read failed", statusCode))
        });
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        Assert.Equal(expectedStatus, preview.FindComponent<OperateSectionStatusPanel>().Instance.Status);
        Assert.Empty(preview.FindAll("[data-preview-unrendered]"));
        Assert.Empty(preview.FindAll("figure.map-preview"));
    }

    [Fact]
    public void InvalidConfiguredApi_DoesNotFallBackToTheLegacyProxy()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient());
        ctx.Services.AddSingleton<IConsolePreviewAddress>(new ConfiguredApiPreviewAddress(new Uri("https://api.example/base")));
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        Assert.NotNull(preview.Find("[data-preview-unrendered]"));
        Assert.Empty(preview.FindAll("figure.map-preview"));
    }

    [Fact]
    public void StaleEnvironment_DoesNotReadPublicationMetadata()
    {
        using var ctx = new BunitContext();
        var reads = 0;
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([new() { Id = "env-2" }]));
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient
        {
            Read = () => { reads++; return Task.FromResult(Publications(new DiscoveryPublicationImpact("pub-wrong-environment", "cadastre", "parcels"))); }
        });
        var preview = ctx.Render<LayerMapPreview>(p => p.Add(x => x.Layer, Parcels).Add(x => x.EnvironmentId, "env-1"));
        Assert.Equal(0, reads);
        Assert.NotNull(preview.Find("[data-preview-unrendered]"));
        Assert.Empty(preview.FindAll("figure.map-preview"));
    }

    private sealed class PublicationClient : IHonuaDiscoveryMetadataClient
    {
        public HonuaAdminEndpointResult<DiscoveryMetadataResponse> Result { get; init; } =
            Publications(new DiscoveryPublicationImpact("pub-other-service", "other", "parcels"), new("pub-cadastre-parcels", "cadastre", "parcels"));
        public Func<Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>>>? Read { get; init; }

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> ReadDiscoveryAsync(
            DiscoveryMetadataAddress address, CancellationToken cancellationToken = default) =>
            Read?.Invoke() ?? Task.FromResult(Result);

        public Task<HonuaAdminEndpointResult<DiscoveryMetadataResponse>> UpdateDiscoveryAsync(
            DiscoveryMetadataAddress address, DiscoveryMetadataUpdateRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public void ConfiguredApiStyle_KeepsTheApiPrefixAndRequiresAnEnvironment()
    {
        var address = new ConfiguredApiPreviewAddress(new Uri("https://api.example/base/"));
        Assert.Equal("https://api.example/base/api/styles/pub-cadastre-parcels.json", address.LayerStyle("env-1", "pub-cadastre-parcels"));
        Assert.Equal("https://api.example/base/api/styles/pub%2Fsegment.json", address.LayerStyle("env-1", "pub/segment"));
        Assert.Null(address.LayerStyle("env-1", " "));
        Assert.Null(address.LayerStyle(null, "pub-cadastre-parcels"));
        Assert.Null(address.LayerStyle(" ", "pub-cadastre-parcels"));
        Assert.Null(new ConfiguredApiPreviewAddress(new Uri("https://api.example/base")).LayerStyle("env-1", "pub-cadastre-parcels"));
        Assert.Null(new ConfiguredApiPreviewAddress(new Uri("https://user@api.example/base/")).LayerStyle("env-1", "pub-cadastre-parcels"));
    }

    [Fact]
    public void SelectedPublication_UsesTheConfiguredApiStyleWhenTheHostPublishesOne()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient());
        ctx.Services.AddSingleton<IConsolePreviewAddress>(new ConfiguredApiPreviewAddress(new Uri("https://api.example/base/")));
        var module = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/map-preview.js");
        module.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p
            .Add(x => x.Selection, Selection)
            .Add(x => x.Layer, Parcels)
            .Add(x => x.EnvironmentId, "env-1"));

        pane.WaitForAssertion(() => Assert.Single(module.Invocations["init"]));
        var arguments = System.Text.Json.JsonSerializer.Serialize(module.Invocations["init"][0].Arguments[1]);
        Assert.Contains("https://api.example/base/api/styles/pub-cadastre-parcels.json", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("/map-proxy/", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedPublication_WithoutAServedLayer_SaysSoInsteadOfDrawingPlaceholderTiles()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IHonuaDiscoveryMetadataClient>(new PublicationClient());
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var pane = ctx.Render<LayerPreviewPane>(p => p.Add(x => x.Selection, Selection));

        Assert.NotNull(pane.Find("[data-preview-unrendered]"));
        Assert.Empty(pane.FindAll("figure.map-preview"));
    }
}
