using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class OperateExportsPageTests
{
    [Fact]
    public void ListShowsAccessReasonCopyUrlAndVersion()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client, clipboard) = (fixture.Context, fixture.Client, fixture.Clipboard);
        client.List = Allowed(new ExportListResponse { Items = [Signed(), Public()] });

        var page = context.Render<OperateExportsPage>();

        var signed = page.Find("[data-export-row='parcels']");
        Assert.Contains("Signed-in", signed.QuerySelector("[data-export-access]")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("The Layer has a row filter.", signed.TextContent, StringComparison.Ordinal);
        Assert.Contains("v2", signed.TextContent, StringComparison.Ordinal);
        var open = page.Find("[data-export-row='roads']");
        Assert.Contains("Public", open.QuerySelector("[data-export-access]")!.TextContent, StringComparison.Ordinal);

        open.QuerySelector("[data-console-copy]")!.Click();
        var copied = Assert.Single(clipboard.Invocations["copyText"]);
        Assert.Equal("https://data.example/roads/collection.json", copied.Arguments[0]);
        V4PagePreview.Capture("operate-exports", page.Markup);
    }

    [Fact]
    public void ListOnServerWithoutExportsSaysSoInsteadOfEmpty()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.List = OperateSectionResult<ExportListResponse>.Denied(OperateSectionStatus.Unsupported, "The connected server does not offer Exports or object-storage Connections yet.");

        var page = context.Render<OperateExportsPage>();

        Assert.Contains("does not offer Exports", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-export-row]"));
    }

    [Fact]
    public void EmptyListPointsToLayers()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.List = Allowed(new ExportListResponse());

        var page = context.Render<OperateExportsPage>();

        Assert.Contains("No Exports", page.Markup, StringComparison.Ordinal);
        Assert.Contains(page.FindAll("a"), link => link.GetAttribute("href")?.EndsWith("/operate/layers", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task UnpublishConfirmsThenReloadsAndReportsTheOutcome()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.List = Allowed(new ExportListResponse { Items = [Signed()] });
        client.Unpublish = Allowed(Signed() with { Status = "unpublished" });
        var page = context.Render<OperateExportsPage>();

        page.Find("[data-export-unpublish='parcels']").Click();
        client.List = Allowed(new ExportListResponse { Items = [Signed() with { Status = "unpublished" }] });
        await page.Find("[data-console-confirm-accept]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        Assert.Equal(["parcels"], client.UnpublishCalls);
        page.WaitForAssertion(() => Assert.Contains("Unpublished parcels.", page.Find("[data-export-notice]").TextContent, StringComparison.Ordinal));
        Assert.True(page.Find("[data-export-unpublish='parcels']").HasAttribute("disabled"));
        Assert.Equal(2, client.ListCalls);
    }

    [Fact]
    public void DetailListsVersionsArtifactsAndWhyNotPublic()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Detail = Allowed(Signed() with
        {
            Versions =
            [
                new ExportVersionResponse
                {
                    Version = "v2", FeatureCount = 1200,
                    Artifacts = [new ExportArtifactResponse { Kind = "geoparquet", Href = "https://server.example/v2/data.parquet", Bytes = 3 * 1024 * 1024 },
                                 new ExportArtifactResponse { Kind = "pmtiles", Href = "https://server.example/v2/tiles.pmtiles", Bytes = 512 }]
                },
                new ExportVersionResponse { Version = "v1" }
            ]
        });

        var page = context.Render<OperateExportDetailPage>(parameters => parameters.Add(p => p.ExportId, "parcels"));

        Assert.Equal("parcels", client.GetCalls.Single());
        Assert.Contains("Why not public", page.Find("[data-export-access]").TextContent, StringComparison.Ordinal);
        var current = page.Find("[data-export-version='v2']");
        Assert.Contains("Current", current.TextContent, StringComparison.Ordinal);
        Assert.Contains("1,200", current.TextContent, StringComparison.Ordinal);
        Assert.Contains("GeoParquet · 3 MB", current.QuerySelector("[data-export-artifact='geoparquet']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("PMTiles · 512 B", current.QuerySelector("[data-export-artifact='pmtiles']")!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Current", page.Find("[data-export-version='v1']").TextContent, StringComparison.Ordinal);
        V4PagePreview.Capture("operate-export-detail", page.Markup);
    }

    [Fact]
    public void DetailOfMissingExportShowsNotFound()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Detail = OperateSectionResult<ExportResponse>.Denied(OperateSectionStatus.Missing, "No Export named gone.");

        var page = context.Render<OperateExportDetailPage>(parameters => parameters.Add(p => p.ExportId, "gone"));

        Assert.Contains("No Export named gone.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-export-unpublish]"));
    }

    [Fact]
    public void NewExportWithoutLayerAsksForOne()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo("operate/exports/new");

        var page = context.Render<OperateExportNewPage>();

        Assert.Empty(page.FindAll("[data-export-create]"));
        Assert.Contains("Open a Layer and choose Export as files", page.Markup, StringComparison.Ordinal);
        Assert.Empty(client.CreateCalls);
    }

    [Fact]
    public void NewExportRejectsAnInvalidIdBeforeSending()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo(
            ExportPresentation.NewExportRoute("county", 3, "Parcels").TrimStart('/'));
        var page = context.Render<OperateExportNewPage>();

        page.Find("[data-export-id]").Input("Parcels 2026");

        Assert.NotNull(page.Find("[data-export-id-error]"));
        Assert.True(page.Find("[data-export-create]").HasAttribute("disabled"));
        Assert.Empty(client.CreateCalls);
    }

    [Fact]
    public void NewExportStartsTheJobAndLinksToTheExport()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Create = OperateSectionResult<ExportResponse>.Allowed(
            new ExportResponse { Id = "parcels-2026", Status = "running" }, message: "Export started; job job-7.");
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo(
            ExportPresentation.NewExportRoute("county", 3, "Parcels").TrimStart('/'));
        var page = context.Render<OperateExportNewPage>();

        Assert.Contains("Parcels", page.Find("[data-export-source]").TextContent, StringComparison.Ordinal);
        page.Find("[data-export-id]").Input("parcels-2026");
        page.Find("[data-export-create]").Click();

        var request = Assert.Single(client.CreateCalls);
        Assert.Equal(new ExportSourceDescriptor { Kind = "layer", ServiceId = "county", LayerId = 3, Name = "Parcels" }, request.Source);
        Assert.Equal("parcels-2026", request.ExportId);
        Assert.Contains("Export started; job job-7.", page.Find("[data-export-started]").TextContent, StringComparison.Ordinal);
        Assert.EndsWith("/operate/exports/parcels-2026", page.Find("[data-export-link]").GetAttribute("href"), StringComparison.Ordinal);
        V4PagePreview.Capture("operate-export-new", page.Markup);
    }

    [Fact]
    public void NewExportShowsTheRefusal()
    {
        using var fixture = new ExportsPageFixture();
        var (context, client) = (fixture.Context, fixture.Client);
        client.Create = OperateSectionResult<ExportResponse>.Denied(OperateSectionStatus.Forbidden, "Sign in again to export.");
        context.Services.GetRequiredService<BunitNavigationManager>().NavigateTo(
            ExportPresentation.NewExportRoute("county", 3, null).TrimStart('/'));
        var page = context.Render<OperateExportNewPage>();

        page.Find("[data-export-create]").Click();

        Assert.Null(Assert.Single(client.CreateCalls).ExportId);
        Assert.Contains("Sign in again to export.", page.Find("[data-export-failed]").TextContent, StringComparison.Ordinal);
    }

    private static OperateSectionResult<T> Allowed<T>(T value) => OperateSectionResult<T>.Allowed(value);

    private static ExportResponse Signed() => new()
    {
        Id = "parcels",
        Title = "Parcels",
        Status = "ready",
        CurrentVersion = "v2",
        Source = new ExportSourceDescriptor { Kind = "layer", ServiceId = "county", LayerId = 3, Name = "Parcels" },
        CollectionUrl = "https://server.example/api/v1/exports/parcels/collection.json",
        NotPublicReason = "The Layer has a row filter."
    };

    private static ExportResponse Public() => new()
    {
        Id = "roads",
        Title = "Roads",
        Status = "ready",
        CurrentVersion = "v1",
        Source = new ExportSourceDescriptor { Kind = "layer", ServiceId = "county", LayerId = 4, Name = "Roads" },
        CollectionUrl = "https://server.example/api/v1/exports/roads/collection.json",
        PublicUrl = "https://data.example/roads/collection.json"
    };
}
