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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);
        NavigateWithPublicationId(ctx, "pub-1");

        var page = ctx.Render<StudioReportBuilderPage>();

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
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);
        NavigateWithPublicationId(ctx, "pub-report-1");

        var page = ctx.Render<StudioReportBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-report-publication", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Monthly infrastructure report", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/published/monthly-infrastructure", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Immutable versions", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ver-2", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ver-1", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBuilder_NewReport_RendersAuthoringSurfaceWithPublishGate()
    {
        var data = new FakeReportDataSource();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);

        var page = ctx.Render<StudioReportBuilderPage>();

        // Click "New report" (the first toolbar button) to open the authoring surface.
        page.Find("button.console-button").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-report-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // A brand-new empty report fails the pre-publish gate: title + at least one panel required.
        Assert.Contains("Resolve before publish", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Give the report a title.", page.Markup, StringComparison.Ordinal);
        // Authoring affordances (bindings, panels, responsive preview, narrative) are present.
        Assert.Contains("Add binding", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Narrative", page.Markup, StringComparison.Ordinal);

        // Design structure: the StudioReportEditor mockup is a three-pane editor — outline · long-form page ·
        // inspector — under an editor header bar with a Publish action and a responsive-preview control.
        Assert.Contains("data-report-outline", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-report-page", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-report-inspector", page.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-report-bar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-report-status-badge=\"draft\"", page.Markup, StringComparison.Ordinal);
        // The default inspector exposes the report document settings (data bindings + presentation).
        Assert.Contains("data-report-bindings", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Allow embedding", page.Markup, StringComparison.Ordinal);
        // Publish action reads "Publish…" for a new draft (mockup header).
        Assert.Contains("Publish…", page.Markup, StringComparison.Ordinal);
        // Preview breakpoint control is present in the header bar (responsive preview before publish).
        Assert.Contains("studio-report-breakpoint", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBuilder_AddPanel_RendersOutlineEntryAndEmbeddedItemInThePage()
    {
        var data = new FakeReportDataSource();
        using var ctx = new Bunit.BunitContext();
        // Adding a panel marks the editor dirty, which arms the <UnsavedChangesGuard/> (a JS module import);
        // run Loose JSInterop so bUnit auto-handles that import.
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);

        var page = ctx.Render<StudioReportBuilderPage>();
        page.Find("button.console-button").Click();

        // Add a panel from the outline ("+ Panel"); a chart panel is created and auto-selected.
        page.WaitForAssertion(
            () => Assert.Contains("+ Panel", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        var addPanel = page.FindAll("button").First(b => b.TextContent.Contains("+ Panel", StringComparison.Ordinal));
        addPanel.Click();

        page.WaitForAssertion(
            () =>
            {
                // The panel shows in the outline AND as an embedded item (figure) in the long-form page.
                Assert.Contains("studio-report-outline-row--item", page.Markup, StringComparison.Ordinal);
                Assert.Contains("data-report-embed=\"chart\"", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Figure 1.", page.Markup, StringComparison.Ordinal);
                // A selected chart panel opens the embedded-item inspector with its Vega-Lite spec.
                Assert.Contains("data-report-inspector=\"panel\"", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Vega-Lite spec", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReportBuilder_PublishedReport_RendersRollbackForInactiveVersionsOnly()
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
                        new StudioReportPublicationVersionView("ver-2", 2, "v2", "hash2", 1, true, "ops@honua.test", DateTimeOffset.UtcNow),
                        new StudioReportPublicationVersionView("ver-1", 1, "v1", "hash1", 0, false, "ops@honua.test", DateTimeOffset.UtcNow.AddDays(-7))
                    ]),
                [])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioReportPublicationDataSource>(data);
        NavigateWithPublicationId(ctx, "pub-report-1");

        var page = ctx.Render<StudioReportBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-report-publication", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The active version cannot be pinned to itself; an earlier immutable version offers rollback.
        Assert.Contains("Roll back to r1", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Roll back to r2", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Edit / republish", page.Markup, StringComparison.Ordinal);
    }

    private static void NavigateWithPublicationId(Bunit.BunitContext ctx, string publicationId)
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
        public Task<StudioReportAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioReportAiCapability.Off);

        public Task<StudioReportGenerationOutcome> GenerateAsync(
            StudioReportEditorState currentState, StudioReportGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioReportGenerationOutcome { Status = StudioReportGenerationStatuses.Unsupported });

        public StudioReportPublicationLoad Load { get; set; } = new(null, []);

        public StudioReportCommandResult Command { get; set; } = new(true, "ok");

        public Task<StudioReportPublicationLoad> LoadAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Load);

        public Task<StudioReportCommandResult> PublishAsync(StudioReportEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<StudioReportCommandResult> RollbackAsync(string publicationId, string targetVersionId, string? expectedEtag = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);

        public Task<StudioReportCommandResult> UpdatePolicyAsync(string publicationId, string visibility, bool embeddable, string? expectedEtag = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Command);
    }
}
