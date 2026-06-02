using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the Studio map builder's style-picker catalog source (#161, honua-server ADR-0048). Verifies
/// the server data source projects the OGC API - Styles list into picker options, maps an endpoint issue onto
/// a capability state (so the builder degrades to the legacy free-form style input rather than fabricating a
/// list), the unsupported (no-server) source surfaces the missing-binding state, and the
/// <see cref="StudioMapStyleCatalog"/> backward-compatibility helper recognises advertised styleIds while
/// keeping a legacy/unknown value selectable.
/// </summary>
public sealed class StudioMapStyleCatalogDataSourceTests
{
    [Fact]
    public async Task ServerSource_ProjectsStyleIdsTitlesAndDefault()
    {
        var client = new StubStylesClient(HonuaAdminEndpointResult<HonuaOgcStylesList>.FromData(
            new HonuaOgcStylesList(
                [new HonuaOgcStyleSummary("topographic", "Topographic"), new HonuaOgcStyleSummary("night", null)],
                "topographic")));
        var source = new HonuaServerStudioMapStyleCatalogDataSource(client);

        var catalog = await source.GetStyleCatalogAsync();

        Assert.Null(catalog.Issue);
        Assert.Equal("topographic", catalog.DefaultStyleId);
        Assert.True(catalog.HasStyles);
        Assert.Collection(
            catalog.Styles,
            first =>
            {
                Assert.Equal("topographic", first.StyleId);
                Assert.Equal("Topographic", first.Label);
            },
            second =>
            {
                Assert.Equal("night", second.StyleId);
                // No title -> label degrades to the bare styleId.
                Assert.Equal("night", second.Label);
            });
    }

    [Fact]
    public async Task ServerSource_Issue_SurfacesCapabilityState_WithStatusCodeInDetail()
    {
        var client = new StubStylesClient(HonuaAdminEndpointResult<HonuaOgcStylesList>.FromIssue(
            new HonuaAdminEndpointIssue("Unsupported", "GET /ogc/styles", "Not exposed.", 404)));
        var source = new HonuaServerStudioMapStyleCatalogDataSource(client);

        var catalog = await source.GetStyleCatalogAsync();

        Assert.NotNull(catalog.Issue);
        Assert.Equal("Unsupported", catalog.Issue!.State);
        Assert.Empty(catalog.Styles);
        Assert.Contains("HTTP 404", catalog.Issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedSource_SurfacesMissingBindingState()
    {
        var source = new UnsupportedStudioMapStyleCatalogDataSource();

        var catalog = await source.GetStyleCatalogAsync();

        Assert.NotNull(catalog.Issue);
        Assert.Equal("Missing binding", catalog.Issue!.State);
        Assert.False(catalog.HasStyles);
    }

    [Fact]
    public void Catalog_IsKnown_RecognisesAdvertisedIds_CaseInsensitive_AndRejectsLegacyValues()
    {
        var catalog = new StudioMapStyleCatalog(
            [new StudioMapStyleOption("topographic", "Topographic")],
            null,
            null);

        Assert.True(catalog.IsKnown("topographic"));
        Assert.True(catalog.IsKnown("TOPOGRAPHIC"));
        // A legacy free-form value that the server does not advertise is NOT "known" — the picker keeps it
        // selectable as a custom option so an existing saved map is never silently rewritten.
        Assert.False(catalog.IsKnown("style:point-red"));
        Assert.False(catalog.IsKnown(null));
        Assert.False(catalog.IsKnown(" "));
    }

    private sealed class StubStylesClient(HonuaAdminEndpointResult<HonuaOgcStylesList> result) : IHonuaOgcStylesClient
    {
        public Uri BaseUri { get; } = new("https://server.example");

        public Task<HonuaAdminEndpointResult<HonuaOgcStylesList>> ListStylesAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
