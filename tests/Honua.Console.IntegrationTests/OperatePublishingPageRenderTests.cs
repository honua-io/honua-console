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

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        Assert.Contains("honua-server#1183", page.Markup, StringComparison.Ordinal);
        // No fabricated matrix rows or reviews are rendered in the unbound state.
        Assert.DoesNotContain("Publication Matrix", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Publication Review", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishingPage_WhenBound_RendersMatrixAndReviewSurface()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new StubPublishingWorkspaceDataSource());

        var page = ctx.RenderComponent<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Publication Matrix", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Matrix renders the resource, its publish target, and the blocker for an unsupported target.
        Assert.Contains("Parcels Feature Service", page.Markup, StringComparison.Ordinal);
        Assert.Contains("OGC API Features", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Vector tiles require a tile cache build", page.Markup, StringComparison.Ordinal);

        // Review renders slot, endpoints, catalog registration, policy, warnings, rollback class,
        // provenance, and the evidence deep links.
        Assert.Contains("Publication Review", page.Markup, StringComparison.Ordinal);
        Assert.Contains("public-default", page.Markup, StringComparison.Ordinal);
        Assert.Contains("https://server.example/ogc/parcels", page.Markup, StringComparison.Ordinal);
        Assert.Contains("reversible", page.Markup, StringComparison.Ordinal);
        Assert.Contains("operator@example", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/operate/jobs/job-parcels-001", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/catalog/cat-parcels", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishingPage_Lookup_RendersReviewVersionsAndDrivesRepublishRollback()
    {
        using var ctx = new Bunit.TestContext();
        var source = new InteractivePublishingWorkspaceDataSource();
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(source);

        var page = ctx.RenderComponent<OperatePublishingPage>();

        // Type a publication id and run the lookup.
        page.Find("input.console-input").Input("pub-parcels");
        page.Find("button.console-button").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Parcels map", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Version history renders prior revisions with a rollback control.
        Assert.Contains("Roll back to rev 1", page.Markup, StringComparison.Ordinal);

        // Roll back to the earlier revision: the data source records the target and the active revision moves.
        page.Find("button.console-button-secondary").Click();
        page.WaitForAssertion(
            () => Assert.Equal("ver-1", source.LastRollbackTarget),
            TimeSpan.FromSeconds(5));
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
