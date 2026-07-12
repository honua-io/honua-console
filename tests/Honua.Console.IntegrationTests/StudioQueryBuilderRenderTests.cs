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
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = ctx.Render<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Query content lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-query-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryBuilder_ListUnsupported_LayersHumanSummaryOverDiagnosticsDisclosure()
    {
        // The saved-query-list capability state (honua-console#311): the first line is plain language and
        // carries no issue ref, while the verbatim contract + tracking issue relocate into the disclosure.
        var listUnsupported = new StudioQueryCapabilityState(
            "Query builder",
            "Unsupported",
            "GET /api/v1/analysis/content/items (saved-query list)",
            "honua-server exposes no saved-query list endpoint, so existing queries cannot be enumerated from live data.",
            Summary: "This server version can't list saved queries yet — open a query by id, or create a new one.",
            IssueRef: "honua-server#1182");
        var data = new FakeQueryDataSource
        {
            Workspace = new StudioQueryWorkspace([], [listUnsupported])
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = ctx.Render<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-diagnostics-summary", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // First line: human, no issue ref, no HTTP code.
        var summary = page.Find("[data-diagnostics-summary]").TextContent;
        Assert.Contains("can't list saved queries", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("#1182", summary, StringComparison.Ordinal);

        // Disclosure: the tracking issue is preserved as a real link, and the contract survives verbatim.
        var issue = page.Find("[data-diagnostics-issue]");
        Assert.Equal("honua-server#1182", issue.TextContent);
        Assert.Equal("https://github.com/honua-io/honua-server/issues/1182", issue.GetAttribute("href"));
        Assert.Contains("GET /api/v1/analysis/content/items (saved-query list)", page.Find("[data-diagnostics-contract]").TextContent, StringComparison.Ordinal);
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
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);

        var page = ctx.Render<StudioQueryBuilderPage>();

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

        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        var page = ctx.Render<StudioQueryBuilderPage>();

        // Click "New blank query" to enter the authoring editor directly (not the from-prompt surface).
        page.WaitForAssertion(
            () => Assert.Contains("New blank query", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        page.FindAll("button").First(b => b.TextContent.Contains("New blank query", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The design mockup (screens-studio-rest StudioQueryBuilder) is a two-pane workbench under an
        // editor bar: glyph · title · draft badge · summary · actions, then a left visual builder and a
        // right generated-SQL + preview pane. Assert that structure is present.
        Assert.Contains("data-query-editor-bar", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-query-workbench", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-query-build", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-query-readout-pane", page.Markup, StringComparison.Ordinal);

        // The summary line reports source/predicate/param/field counts for the active draft.
        var summary = page.Find("[data-query-summary]").TextContent;
        Assert.Contains("1 source", summary, StringComparison.Ordinal);
        Assert.Contains("1 predicate", summary, StringComparison.Ordinal);

        // The generated SQL/filter readout reflects the bound source + predicate before save (AC#2).
        Assert.Contains("data-query-readout", page.Markup, StringComparison.Ordinal);
        var readoutText = page.Find("[data-query-readout]").TextContent;
        Assert.Equal("SELECT * FROM permits/layer/5 WHERE status = 'approved'", readoutText);

        // Before a preview is pulled, the right pane shows the explicit empty-preview placeholder rather
        // than a fabricated table.
        Assert.Contains("data-query-preview-empty", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-query-preview-table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryBuilder_NewFromPrompt_RendersConversationAndHydratesGeneratedQuery()
    {
        // New-from-prompt opens the StudioAiConversation surface; a send drives the server NL-generation
        // contract and, on a 'generated' outcome, hydrates the right-side query inspector from the proposal.
        var data = new FakeQueryDataSource
        {
            NewQuery = new StudioQueryEditor(),
            GenerateOutcome = current =>
            {
                current.ServiceName = "permits";
                current.LayerId = 9;
                current.Predicates.Add(new StudioQueryPredicateEditor { Field = "status", Operator = "=", Value = "approved" });
                current.OutFields.Add("permit_id");
                return new StudioQueryGenerationOutcome
                {
                    Status = StudioQueryGenerationStatuses.Generated,
                    Query = current,
                    Rationale = "Proposed a permits-in-flood-zones query."
                };
            }
        };

        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        var page = ctx.Render<StudioQueryBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("New from prompt", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        page.FindAll("button").First(b => b.TextContent.Contains("New from prompt", StringComparison.Ordinal)).Click();

        // The from-prompt conversation surface is up (not the raw editor).
        page.WaitForAssertion(
            () => Assert.Contains("data-query-ai", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Type a prompt and send it; the generated outcome hydrates the inspector from the real editor state.
        page.Find("textarea.studio-ai-refine-input").Input("approved permits in flood zones");
        page.FindAll("button").First(b => b.TextContent.Contains("Send", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("Proposed a permits-in-flood-zones query.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The inspector reflects the server-proposed query (never fabricated), including the generated SQL.
        var readout = page.Find("[data-query-ai-readout]").TextContent;
        Assert.Contains("permits/layer/9", readout, StringComparison.Ordinal);
        Assert.Contains("status = 'approved'", readout, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryBuilder_OpenAndPreview_RendersLiveMapTablePreview()
    {
        var existing = new StudioQueryEditor { QueryId = "query-7", Version = 3, Title = "Flood permits", ServiceName = "permits", LayerId = 5 };
        var previewed = new StudioQueryEditor { QueryId = "query-7", Version = 3, Title = "Flood permits", ServiceName = "permits", LayerId = 5 };
        previewed.Parameters.Add(new StudioQueryParameterEditor { Name = "minYear", Value = "2024" });
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

        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioQueryPackageDataSource>(data);
        var page = ctx.Render<StudioQueryBuilderPage>();

        // Open the seeded query from the list.
        page.WaitForAssertion(
            () => Assert.Contains("Flood permits", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        page.FindAll("button").First(b => b.TextContent.Contains("Flood permits", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-editor", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Preview pulls the live map/table preview from the data source. The mockup labels this "Run preview".
        page.FindAll("button").First(b => b.TextContent.Contains("Run preview", StringComparison.Ordinal)).Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-query-preview-table", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("data-query-downstream", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Map layer", page.Markup, StringComparison.Ordinal);

        // The preview pane mirrors the mockup: a parameter readout chip ($minYear=2024) on the preview
        // header and an EXPLAIN-style footer carrying the live preview artifact id.
        var previewParams = page.Find("[data-query-preview-params]").TextContent;
        Assert.Contains("$minYear=2024", previewParams, StringComparison.Ordinal);
        var explain = page.Find("[data-query-explain]").TextContent;
        Assert.Contains("EXPLAIN", explain, StringComparison.Ordinal);
        Assert.Contains("preview-1", explain, StringComparison.Ordinal);
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

        public Func<StudioQueryEditor, StudioQueryGenerationOutcome>? GenerateOutcome { get; init; }

        public Task<StudioQueryGenerationOutcome> GenerateAsync(
            StudioQueryEditor currentQuery,
            StudioQueryGenerationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerateOutcome?.Invoke(currentQuery)
                ?? new StudioQueryGenerationOutcome { Status = StudioQueryGenerationStatuses.Unsupported, Rationale = "Generation not configured for this test." });
    }
}
