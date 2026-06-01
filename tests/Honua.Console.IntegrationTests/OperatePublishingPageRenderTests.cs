using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Operate publishing workspace (<c>/operate/publishing</c>).
/// Covers the merged-runtime missing-binding state (no honua-server configured, no mock data) and the
/// server-bound state where the matrix and review surface render the slot, generated endpoints,
/// catalog registration, policy, warnings, rollback class, provenance, and evidence deep links.
/// </summary>
public sealed class OperatePublishingPageRenderTests
{
    [Fact]
    public void OperatePublishingPage_WhenUnbound_RendersMissingBindingWithoutMockData()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-server#1183", page.Markup, StringComparison.Ordinal);
        // No fabricated matrix rows or reviews are rendered in the unbound state.
        Assert.DoesNotContain("Publication Matrix", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Publication Review", page.Markup, StringComparison.Ordinal);

        // The design IA (mode bar, stepper, conceptual flow map) renders even unbound — it is
        // static workflow guidance, not fabricated publication data.
        Assert.Contains("publish-mode-bar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Quick publish", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Author resource first", page.Markup, StringComparison.Ordinal);
        Assert.Contains("publish-stepper", page.Markup, StringComparison.Ordinal);
        Assert.Contains("publish-flow-map", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishingPage_ModeToggle_SwapsStepperBetweenQuickAndAuthorFirst()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("publish-stepper", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Quick flow stepper: Service -> Layer -> Review.
        var stepper = page.Find("ol.publish-stepper");
        Assert.Contains("Service", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Layer", stepper.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Projection", stepper.TextContent, StringComparison.Ordinal);

        // Switch to the advanced author-first mode -> the seven-step flow appears.
        var authorFirst = page.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal));
        authorFirst.Click();

        page.WaitForAssertion(
            () => Assert.Contains("Projection", page.Find("ol.publish-stepper").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        var advanced = page.Find("ol.publish-stepper");
        Assert.Contains("Target", advanced.TextContent, StringComparison.Ordinal);
        Assert.Contains("Compatibility", advanced.TextContent, StringComparison.Ordinal);
        Assert.Contains("Access", advanced.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishingPage_WhenBound_RendersMatrixAndReviewSurface()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new StubPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Publication Matrix", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Matrix renders the resource, its publish target, and the blocker for an unsupported target.
        Assert.Contains("Parcels Feature Service", page.Markup, StringComparison.Ordinal);
        Assert.Contains("OGC API Features", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Vector tiles require a tile cache build", page.Markup, StringComparison.Ordinal);

        // Matrix support badges carry the support-state styling so blockers read as blockers.
        Assert.Contains("console-state-danger", page.Markup, StringComparison.Ordinal);

        // Review renders slot, endpoints, catalog registration, policy, warnings, rollback class,
        // provenance, and the evidence deep links.
        Assert.Contains("Publication Review", page.Markup, StringComparison.Ordinal);
        Assert.Contains("public-default", page.Markup, StringComparison.Ordinal);
        Assert.Contains("https://server.example/ogc/parcels", page.Markup, StringComparison.Ordinal);
        Assert.Contains("reversible", page.Markup, StringComparison.Ordinal);
        Assert.Contains("operator@example", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/jobs/job-parcels-001", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/catalog/cat-parcels", page.Markup, StringComparison.Ordinal);

        // Review is the layered creation stack from the design handoff: Data Resource binds to a
        // service slot which mirrors to a catalog entry.
        Assert.Contains("publish-review-stack", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Data Resource", page.Markup, StringComparison.Ordinal);
        Assert.Contains("binds to", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Service slot", page.Markup, StringComparison.Ordinal);
        Assert.Contains("mirrors to", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Catalog registration", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Public sharing exposes all attributes.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishingPage_Lookup_RendersReviewVersionsAndDrivesRepublishRollback()
    {
        using var ctx = new Bunit.TestContext();
        var source = new InteractivePublishingWorkspaceDataSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        // Type a publication id and run the lookup. Scope to the lookup section: the functional
        // publish wizards now render their own console-input / console-button controls above it.
        var lookup = page.Find("[data-publication-lookup]");
        lookup.QuerySelector("input.console-input")!.Input("pub-parcels");
        lookup.QuerySelector("button.console-button")!.Click();

        page.WaitForAssertion(
            () => Assert.Contains("Parcels map", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Version history renders prior revisions with a rollback control.
        Assert.Contains("Roll back to rev 1", page.Markup, StringComparison.Ordinal);

        // Roll back to the earlier revision: the data source records the target and the active revision moves.
        page.Find("[data-publication-lookup] button.console-button-secondary").Click();
        page.WaitForAssertion(
            () => Assert.Equal("ver-1", source.LastRollbackTarget),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OperatePublishingPage_MalformedLookupId_ShowsInlineError_AndGatesReview()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new InteractivePublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        var lookup = page.Find("[data-publication-lookup]");
        lookup.QuerySelector("input.console-input")!.Input("bad id");

        page.WaitForAssertion(
            () => Assert.Contains("Publication id may only contain", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
        // The Review button (the lookup section's primary console-button) is gated on the blocking finding.
        Assert.True(page.Find("[data-publication-lookup] button.console-button").HasAttribute("disabled"));
    }

    private sealed class InteractivePublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
    {
        public string? LastRollbackTarget { get; private set; }

        public Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishingWorkspace([], [], []));

        public Task<PublishingLookupResult> LookupAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(activeRevision: 2));

        public Task<PublishingLookupResult> RepublishAsync(
            string publicationId,
            PublishingRepublishCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(activeRevision: 3));

        public Task<PublishingLookupResult> RollbackAsync(
            string publicationId,
            PublishingRollbackCommand command,
            CancellationToken cancellationToken = default)
        {
            LastRollbackTarget = command.TargetVersionId;
            return Task.FromResult(Result(activeRevision: 1));
        }

        private static PublishingLookupResult Result(long activeRevision) =>
            new(
                new PublishingReview(
                    "pub-parcels",
                    "Parcels map",
                    PublishingResourceKind.StudioArtifact,
                    "parcels (rev " + activeRevision + ")",
                    [new PublishingEndpoint("Published map", "/published/parcels")],
                    new PublishingCatalogRegistration(true, "pub-parcels", "public"),
                    "visibility: public",
                    [],
                    "reversible",
                    "operator@honua.test",
                    new PublishingReviewLinks("/operate/jobs/job-9", null, "/operate/audit/aud-9", null)),
                [
                    new PublishingVersion("ver-2", 2, "Parcels map", null, activeRevision == 2, "operator@honua.test", DateTimeOffset.UnixEpoch),
                    new PublishingVersion("ver-1", 1, "Parcels map", null, activeRevision == 1, "operator@honua.test", DateTimeOffset.UnixEpoch)
                ],
                []);
    }

    private sealed class StubPublishingWorkspaceDataSource : IPublishingWorkspaceDataSource
    {
        public Task<PublishingLookupResult> LookupAsync(string publicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishingLookupResult(null, [], []));

        public Task<PublishingLookupResult> RepublishAsync(
            string publicationId,
            PublishingRepublishCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishingLookupResult(null, [], []));

        public Task<PublishingLookupResult> RollbackAsync(
            string publicationId,
            PublishingRollbackCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishingLookupResult(null, [], []));

        public Task<PublishingWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PublishingWorkspace(
                Matrix:
                [
                    new PublishingMatrixRow(
                        "svc-parcels",
                        "Parcels Feature Service",
                        PublishingResourceKind.Service,
                        [
                            new PublishingMatrixTarget("OGC API Features", PublishingTargetSupport.Supported, null),
                            new PublishingMatrixTarget(
                                "Vector tiles",
                                PublishingTargetSupport.Blocked,
                                "Vector tiles require a tile cache build."),
                        ])
                ],
                Reviews:
                [
                    new PublishingReview(
                        "svc-parcels",
                        "Parcels Feature Service",
                        PublishingResourceKind.Service,
                        "public-default",
                        [new PublishingEndpoint("OGC API Features", "https://server.example/ogc/parcels")],
                        new PublishingCatalogRegistration(true, "cat-parcels", "public"),
                        "open-data publish policy",
                        ["Public sharing exposes all attributes."],
                        "reversible",
                        "operator@example at 2026-05-29T12:00:00Z",
                        new PublishingReviewLinks(
                            "/operate/jobs/job-parcels-001",
                            "/operate/events/evt-parcels-001",
                            "/operate/audit/aud-parcels-001",
                            "/operate/rollback/rb-parcels-001"))
                ],
                CapabilityStates: []));
    }
}
