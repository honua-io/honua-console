using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Studio query builder page (<c>/studio/query</c>, honua-console#52).
/// Verifies the missing-binding surface when no honua-server is configured, the bound workspace package
/// list, and the authoring editor: source binding, predicate builder, generated SQL/filter readout, and the
/// live map/table preview. Drives the page through a fake <see cref="IStudioQueryPackageDataSource"/> rather
/// than a mock server, so it stays in the Docker-free lane.
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

    [Fact]
    public void QueryBuilder_NewQuery_RendersEditorWithGeneratedReadout()
    {
        var template = new StudioQueryEditor { ServiceName = "permits", LayerId = 5 };
        template.Predicates.Add(new StudioQueryPredicateEditor { Field = "status", Operator = "=", Value = "approved" });
        var data = new FakeQueryDataSource { NewQuery = template };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        var page = ctx.RenderComponent<StudioQueryBuilderPage>();

        // Click "New query" to enter the authoring editor.
        page.WaitForAssertion(
            () => Assert.Contains("New query", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        page.Find("button.console-button").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The generated SQL/filter readout reflects the bound source + predicate before save (AC#2).
        Assert.Contains("data-query-readout", page.Markup, StringComparison.Ordinal);
        var readoutText = page.Find("[data-query-readout]").TextContent;
        Assert.Equal("SELECT * FROM permits/layer/5 WHERE status = 'approved'", readoutText);
    }

    [Fact]
    public void QueryBuilder_OpenAndPreview_RendersLiveMapTablePreview()
    {
        var existing = new StudioQueryEditor { QueryId = "query-7", Version = 3, Title = "Flood permits", ServiceName = "permits", LayerId = 5 };
        var previewed = new StudioQueryEditor { QueryId = "query-7", Version = 3, Title = "Flood permits", ServiceName = "permits", LayerId = 5 };
        previewed.Preview = new StudioQueryPreview(
            "preview-1",
            5,
            TotalCount: 1,
            ExceededPreviewLimit: false,
            Features: [new StudioQueryPreviewFeatureView(1, true, new Dictionary<string, string> { ["status"] = "approved" })],
            Columns: ["status"],
            DownstreamTargets: ["map", "dashboard", "workflow"]);

        var data = new FakeQueryDataSource
        {
            Workspace = new StudioQueryWorkspace(
                [new StudioQueryPackageListItem("query-7", "Flood permits", "permits/5", 3, null, DateTimeOffset.UnixEpoch)],
                []),
            OpenQuery = existing,
            PreviewResult = new StudioQueryCommandResult(true, "Previewed 1 of 1 feature(s).", previewed)
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        var page = ctx.RenderComponent<StudioQueryBuilderPage>();

        // Open the seeded query from the list.
        page.WaitForAssertion(
            () => Assert.Contains("Flood permits", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        page.FindAll("button").First(b => b.TextContent.Contains("Flood permits", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Preview pulls the live map/table preview from the data source.
        page.FindAll("button").First(b => b.TextContent.Contains("Preview map/table", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-preview-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-query-downstream", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Map layer", page.Markup, StringComparison.Ordinal);
    }

    private sealed class FakeQueryDataSource : IStudioQueryPackageDataSource
    {
        public StudioQueryWorkspace Workspace { get; init; } = new([], []);

        public StudioQueryEditor? NewQuery { get; init; }

        public StudioQueryEditor? OpenQuery { get; init; }

        public StudioQueryCommandResult PreviewResult { get; init; } = new(false, "not configured");

        public Task<StudioQueryWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

        public Task<StudioQueryEditorLoad> LoadAsync(string? queryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.IsNullOrWhiteSpace(queryId)
                ? new StudioQueryEditorLoad(NewQuery ?? new StudioQueryEditor(), [])
                : new StudioQueryEditorLoad(OpenQuery ?? new StudioQueryEditor { QueryId = queryId }, []));

        public Task<StudioQueryCommandResult> SaveAsync(StudioQueryEditor query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioQueryCommandResult(true, "Saved.", query));

        public Task<StudioQueryCommandResult> PreviewAsync(StudioQueryEditor query, CancellationToken cancellationToken = default) =>
            Task.FromResult(PreviewResult);
    }
}
