using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperatePublishingPageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatePublishingPage_WhenUnbound_RendersMissingBindingWithoutMockData(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-server#1183", page.Markup, StringComparison.Ordinal);
        // No fabricated matrix rows or reviews are rendered in the unbound state.
        Assert.DoesNotContain("Publishing formats", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Publishing review", page.Markup, StringComparison.Ordinal);

        // The mode bar and flow map remain available; failed reads show one recovery surface.
        Assert.Contains("publish-mode-bar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Quick publish", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Author dataset first", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("ol.publish-stepper"));
        Assert.Single(page.FindAll("[data-connection-notice]"));
        Assert.Contains("publish-flow-map", page.Markup, StringComparison.Ordinal);

        if (enabled)
        {
            Assert.Equal("/operate/publishing", ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Route);
            CatalogAdminPreview.Capture("data-publishing-unbound", page.Markup);
        }
    }

    [Fact]
    public void OperatePublishingPage_ModeToggle_PreservesRecoveryAndDeclaresAuthorFirstUnsupported()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll("[data-connection-notice]")),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("ol.publish-stepper"));

        // Switch to the advanced author-first mode. Its seven steps were a mockup — a compatibility
        // matrix, a slot id, field aliases, an access summary and a projection preview, none of which
        // any server contract supplies — so the mode now declares itself unwired instead of rendering
        // them. The stepper disappears with it.
        var authorFirst = page.FindAll("button.publish-mode-option")
            .Single(b => b.TextContent.Contains("Author dataset first", StringComparison.Ordinal));
        authorFirst.Click();

        page.WaitForAssertion(
            () => Assert.Contains("This flow is not wired yet", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("ol.publish-stepper"));
        Assert.DoesNotContain("Compatibility", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatePublishingPage_WhenBound_RendersMatrixAndReviewSurface(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new StubPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Publishing formats", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Matrix renders the resource, its publish target, and the blocker for an unsupported target.
        Assert.Contains("Parcels Feature Service", page.Markup, StringComparison.Ordinal);
        Assert.Contains("OGC API Features", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Vector tiles require a tile cache build", page.Markup, StringComparison.Ordinal);

        // Matrix support badges carry the support-state styling so blockers read as blockers.
        Assert.Contains("console-state-danger", page.Markup, StringComparison.Ordinal);

        // Review renders slot, endpoints, catalog registration, policy, warnings, rollback class,
        // provenance, and the evidence deep links.
        Assert.Contains("Publishing review", page.Markup, StringComparison.Ordinal);
        // Both modes keep the full format guidance and the lookup instructions.
        Assert.Contains("Supported output and catalog formats for each object. Review any unsupported or blocked format before publishing.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Enter a release ID to review its route, release a new version, or restore a previous version.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("public-default", page.Markup, StringComparison.Ordinal);
        Assert.Contains("https://server.example/ogc/parcels", page.Markup, StringComparison.Ordinal);
        Assert.Contains("reversible", page.Markup, StringComparison.Ordinal);
        Assert.Contains("operator@example", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/jobs/job-parcels-001", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/catalog/cat-parcels", page.Markup, StringComparison.Ordinal);

        var review = page.Find("[data-publishing-review]");
        Assert.Equal("Service", review.QuerySelector("article .console-kicker")!.TextContent);
        Assert.Equal("Route", review.QuerySelector(".publish-review-layer .console-kicker")!.TextContent);
        Assert.Contains("binds to", review.TextContent, StringComparison.Ordinal);
        Assert.Contains("mirrors to", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Catalog registration", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Public sharing exposes all attributes.", page.Markup, StringComparison.Ordinal);

        if (enabled)
        {
            Assert.Equal("/operate/publishing", ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Route);
            CatalogAdminPreview.Capture("data-publishing", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatePublishingPage_Lookup_RendersReviewVersionsAndDrivesRepublishRollback(bool enabled)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        ctx.Services.AddSingleton<ConsoleAgentContextRegistry>();
        var source = new InteractivePublishingWorkspaceDataSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

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

        // Roll back to the earlier revision. The destructive action is now gated behind a confirm
        // dialog (UX hardening): clicking the rollback control only opens the dialog; accepting it runs
        // the rollback, records the target, and moves the active revision.
        page.Find("[data-publication-lookup] button.console-button-secondary").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-console-confirm-accept]")),
            TimeSpan.FromSeconds(5));
        page.Find("[data-console-confirm-accept]").Click();
        page.WaitForAssertion(
            () => Assert.Equal("ver-1", source.LastRollbackTarget),
            TimeSpan.FromSeconds(5));

        if (enabled)
        {
            Assert.Equal("/operate/publishing", ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Route);
            Assert.Equal(0, ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
            page.Find("input[aria-label='Republish title']").Input("Next release");
            Assert.Equal(1, ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
            page.Find("input[aria-label='Republish title']").Input("");
            Assert.Equal(0, ctx.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
            CatalogAdminPreview.Capture("data-publishing-lookup", page.Markup);
        }
    }

    [Fact]
    public void OperatePublishingPage_MalformedLookupId_ShowsInlineError_AndGatesReview()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new InteractivePublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

        var lookup = page.Find("[data-publication-lookup]");
        lookup.QuerySelector("input.console-input")!.Input("bad id");

        page.WaitForAssertion(
            () => Assert.Contains("Release ID may only contain", page.Markup, StringComparison.Ordinal),
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
