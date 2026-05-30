using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Studio query builder page (<c>/studio/query</c>, honua-console#52).
/// Verifies the missing-binding surface when no honua-server is configured, and that a bound workspace
/// renders the saved query package list. Drives the page through a fake <see cref="IStudioQueryPackageDataSource"/>
/// rather than a mock server, so it stays in the Docker-free lane.
/// </summary>
public sealed class StudioQueryBuilderRenderTests
{
    private static readonly StudioQueryCapabilityState MissingBinding = new(
        "Query builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the query builder can bind honua-server#1182.");

    [Fact]
    public void QueryBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeQueryDataSource
        {
            Workspace = new StudioQueryWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Query content lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-query-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryBuilder_WhenBound_RendersSavedQueryPackageList()
    {
        var data = new FakeQueryDataSource
        {
            Workspace = new StudioQueryWorkspace(
                [
                    new StudioQueryPackageListItem(
                        "query-flood-permits",
                        "Flood-zone permits",
                        "content:permits@v3",
                        DraftVersion: 2,
                        PublishedVersion: 1,
                        UpdatedAt: DateTimeOffset.UnixEpoch)
                ],
                [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Flood-zone permits", page.Markup, StringComparison.Ordinal);
        Assert.Contains("content:permits@v3", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("is not bound", page.Markup, StringComparison.Ordinal);
    }

    private sealed class FakeQueryDataSource : IStudioQueryPackageDataSource
    {
        public StudioQueryWorkspace Workspace { get; init; } = new([], []);

        public Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);
    }
}
