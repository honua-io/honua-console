using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the report builder page (<c>/studio/report</c>). Verifies the
/// missing-binding surface and that an opened report publication renders its server-owned route and
/// immutable version history. Drives the page through a fake <see cref="IStudioReportPublicationDataSource"/>
/// rather than a mock server, in line with the form builder render-test pattern; the live server binding
/// ships its own opt-in Testcontainers suite.
/// </summary>
public sealed class StudioReportBuilderRenderTests
{
    [Fact]
    public void ReportBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeReportDataSource
        {
            Load = new StudioReportPublicationLoad(null, [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);
        NavigateWithPublicationId(ctx, "pub-1");

        var page = ctx.RenderComponent<StudioReportBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Report publication registry is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-report-publication", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBuilder_OpenReportPublication_RendersRouteAndVersionHistory()
    {
        var data = new FakeReportDataSource
        {
            Load = new StudioReportPublicationLoad(
                new StudioReportPublicationView(
                    PublicationId: "pub-report-1",
                    RouteSlug: "monthly-infrastructure",
                    RoutePath: "/published/monthly-infrastructure",
                    Kind: "report",
                    Lifecycle: "active",
                    Visibility: "organization",
                    Embeddable: true,
                    ActiveTitle: "Monthly infrastructure report",
                    ActiveVersionId: "ver-2",
                    ActiveRevision: 2,
                    PreviousVersionId: "ver-1",
                    RollbackTargetVersionId: null,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    Versions:
                    [
                        new StudioReportPublicationVersionView("ver-2", 2, "Monthly infrastructure report", "hash2", 1, true, "ops@honua.test", DateTimeOffset.UtcNow),
                        new StudioReportPublicationVersionView("ver-1", 1, "Initial report", "hash1", 0, false, "ops@honua.test", DateTimeOffset.UtcNow.AddDays(-7))
                    ]),
                [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);
        NavigateWithPublicationId(ctx, "pub-report-1");

        var page = ctx.RenderComponent<StudioReportBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-report-publication", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Monthly infrastructure report", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/published/monthly-infrastructure", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Immutable versions", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ver-2", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ver-1", page.Markup, StringComparison.Ordinal);
    }

    private static void NavigateWithPublicationId(Bunit.TestContext ctx, string publicationId)
    {
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("publicationId", publicationId));
    }

    private static readonly StudioReportCapabilityState MissingBinding = new(
        "Report builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the report builder can bind the server-owned content publication registry.");

    private sealed class FakeReportDataSource : IStudioReportPublicationDataSource
    {
        public StudioReportPublicationLoad Load { get; set; } = new(null, []);

        public Task<StudioReportPublicationLoad> LoadAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);
    }
}
