using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render + interaction coverage for <see cref="PublishWizardWorkspace"/>, the publish
/// wizard on the Operate publishing workspace.
/// </summary>
/// <remarks>
/// <para>
/// These tests exist because the wizard used to be a mockup that made one real call. It carried a
/// <c>prod-postgis</c> connection constant, a <c>public.parcels_2024</c> table, a seeded service
/// tree, and review panels quoting a feature count, a field count, a PII flag, a layer slot and a
/// published URL — none of it read from a server. The finish action then built its genuine publish
/// command from those constants, so publishing 404'd on every deployment that did not happen to own
/// a connection with that exact name.
/// </para>
/// <para>
/// The suite therefore asserts two things the old tests could not: that every rendered value comes
/// from the stubbed data sources, and that the publish command carries the operator's selection. The
/// last test is a fixture guard — it fails if any retired mockup literal reappears in the markup.
/// </para>
/// </remarks>
public sealed class PublishWizardWorkspaceRenderTests
{
    private static Bunit.BunitContext NewContext(
        IOperateTransitionDataSource? operateData = null,
        IServiceLayerPublishOperation? publishOperation = null)
    {
        var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(operateData ?? new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton(publishOperation ?? new UnsupportedServiceLayerPublishOperation());
        return ctx;
    }

    [Fact]
    public void ServiceStep_ListsTheServersServices_NotASeededTree()
    {
        using var ctx = NewContext(StubOperateData.WithFixtures());
        var cut = ctx.Render<PublishWizardWorkspace>();

        var stepper = cut.Find("ol.publish-stepper");
        Assert.Contains("Service", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Layer", stepper.TextContent, StringComparison.Ordinal);
        Assert.Contains("Review", stepper.TextContent, StringComparison.Ordinal);

        // The tree rows are the stub's services, and nothing else.
        var rows = cut.FindAll("[data-publish-tree-row]");
        Assert.Equal(2, rows.Count);
        Assert.Contains("hydrology-fs", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("FeatureServer", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2 layers", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("basemaps-ms", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceStep_WithNoServicesBound_RendersAnEmptyStateRatherThanSampleRows()
    {
        using var ctx = NewContext();
        var cut = ctx.Render<PublishWizardWorkspace>();

        Assert.Empty(cut.FindAll("[data-publish-tree-row]"));
        Assert.NotNull(cut.Find("[data-publish-tree-empty]"));
    }

    [Fact]
    public void ServiceRail_ReportsTheSelectedServicesOwnFacts()
    {
        using var ctx = NewContext(StubOperateData.WithFixtures());
        var cut = ctx.Render<PublishWizardWorkspace>();

        cut.FindAll("[data-publish-tree-row]")[0].Click();

        var rail = cut.Find(".publish-wizard-rail").TextContent;
        Assert.Contains("You're publishing into", rail, StringComparison.Ordinal);
        Assert.Contains("hydrology-fs", rail, StringComparison.Ordinal);
        Assert.Contains("running", rail, StringComparison.Ordinal);

        // The old rail asserted a CRS, an anonymous-access flag and a capability list that no
        // contract supplied. Nothing may claim them now.
        Assert.DoesNotContain("EPSG:4326", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("Query, Extract", rail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayerStep_ListsTablesForTheChosenConnection()
    {
        var publish = new RecordingPublishOperation();
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();

        await SelectServiceAndAdvanceAsync(cut);

        cut.Find("[data-connection-picker]").Change("conn-a");

        var options = cut.FindAll("[data-table-picker] option");
        Assert.Equal("conn-a", publish.LastListedConnectionId);
        // Placeholder + the two tables the stub reported for that connection.
        Assert.Equal(3, options.Count);
        Assert.Contains("hydro.streams", options[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("hydro.basins", options[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayerRail_DescribesTheSelectedTableFromTheServersOwnMetadata()
    {
        using var ctx = NewContext(StubOperateData.WithFixtures(), new RecordingPublishOperation());
        var cut = ctx.Render<PublishWizardWorkspace>();

        await SelectServiceAndAdvanceAsync(cut);
        cut.Find("[data-connection-picker]").Change("conn-a");
        cut.Find("[data-table-picker]").Change("hydro.streams");

        var rail = cut.Find(".publish-wizard-rail").TextContent;
        Assert.Contains("hydro.streams", rail, StringComparison.Ordinal);
        Assert.Contains("the_geom", rail, StringComparison.Ordinal);
        Assert.Contains("LineString", rail, StringComparison.Ordinal);
        Assert.Contains("3857", rail, StringComparison.Ordinal);
        Assert.Contains("4,096", rail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_SendsTheOperatorsSelection()
    {
        var publish = new RecordingPublishOperation();
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();

        await SelectServiceAndAdvanceAsync(cut);
        cut.Find("[data-connection-picker]").Change("conn-a");
        cut.Find("[data-table-picker]").Change("hydro.streams");
        await ContinueAsync(cut);
        await FinishAsync(cut);

        var command = Assert.Single(publish.Commands);
        Assert.Equal("conn-a", command.ConnectionId);
        Assert.Equal("hydro", command.Schema);
        Assert.Equal("streams", command.Table);
        Assert.Equal("streams", command.LayerName);
        Assert.Equal("hydrology-fs", command.ServiceName);
        Assert.Equal("the_geom", command.GeometryColumn);
        Assert.Equal("LineString", command.GeometryType);
        Assert.Equal(3857, command.Srid);
        Assert.Equal(["id", "name", "the_geom"], command.Fields);
    }

    [Fact]
    public async Task ReviewStep_LeavesServerAssignedFactsUnclaimedUntilThePublishLands()
    {
        using var ctx = NewContext(StubOperateData.WithFixtures(), new RecordingPublishOperation());
        var cut = ctx.Render<PublishWizardWorkspace>();

        await SelectServiceAndAdvanceAsync(cut);
        cut.Find("[data-connection-picker]").Change("conn-a");
        cut.Find("[data-table-picker]").Change("hydro.streams");
        await ContinueAsync(cut);

        var review = cut.Find("[data-quick-step=\"review\"]").TextContent;
        Assert.Contains("assigned by the server on publish", review, StringComparison.Ordinal);
        Assert.Contains("assigned on publish", review, StringComparison.Ordinal);

        // No URL is previewed before the server has assigned the slot.
        Assert.Empty(cut.FindAll("[data-url-preview]"));
    }

    [Fact]
    public async Task Publish_SurfacesTheServersRejectionInsteadOfClaimingSuccess()
    {
        var publish = new RecordingPublishOperation(new ServiceLayerPublishResult
        {
            Succeeded = false,
            State = "Not found",
            Detail = "The requested resource was not found."
        });
        using var ctx = NewContext(StubOperateData.WithFixtures(), publish);
        var cut = ctx.Render<PublishWizardWorkspace>();

        await SelectServiceAndAdvanceAsync(cut);
        cut.Find("[data-connection-picker]").Change("conn-a");
        cut.Find("[data-table-picker]").Change("hydro.streams");
        await ContinueAsync(cut);
        await FinishAsync(cut);

        var callout = cut.Find("[data-publish-result]");
        Assert.Equal("not-found", callout.GetAttribute("data-publish-state"));
        Assert.Contains("was not found", callout.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorFirstMode_DeclaresItselfUnwiredRatherThanRenderingInventedSteps()
    {
        using var ctx = NewContext(StubOperateData.WithFixtures());
        var cut = ctx.Render<PublishWizardWorkspace>();

        cut.FindAll(".publish-mode-option")
            .Single(b => b.TextContent.Contains("Author resource first", StringComparison.Ordinal))
            .Click();

        Assert.Contains("This flow is not wired yet", cut.Markup, StringComparison.Ordinal);

        // The retired author-first steps invented a compatibility matrix, a slot id, field aliases,
        // an access summary and a projection preview. None of them may render.
        Assert.Empty(cut.FindAll("[data-author-step]"));
        Assert.DoesNotContain("data-projection-controls", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("map-preview-labels", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wizard_NeverRendersTheRetiredMockupFixtures()
    {
        // The fixture guard. Each literal below was rendered by the pre-fix wizard as though it were
        // live publication state; a regression that reintroduces any of them fails here rather than
        // reaching an operator.
        string[] retired =
        [
            "prod-postgis",
            "parcels_2024",
            "public-works-fs",
            "1,284,021",
            "honua.example.gov",
            "PII flagged",
            "owner_name",
            "Cache TTL",
            "1:500",
        ];

        using var ctx = NewContext(StubOperateData.WithFixtures(), new RecordingPublishOperation());
        var cut = ctx.Render<PublishWizardWorkspace>();

        var markup = cut.Markup;

        await SelectServiceAndAdvanceAsync(cut);
        cut.Find("[data-connection-picker]").Change("conn-a");
        cut.Find("[data-table-picker]").Change("hydro.streams");
        markup += cut.Markup;

        await ContinueAsync(cut);
        markup += cut.Markup;

        foreach (var literal in retired)
        {
            Assert.DoesNotContain(literal, markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task SelectServiceAndAdvanceAsync(IRenderedComponent<PublishWizardWorkspace> cut)
    {
        cut.FindAll("[data-publish-tree-row]")[0].Click();
        await ContinueAsync(cut);
    }

    private static Task ContinueAsync(IRenderedComponent<PublishWizardWorkspace> cut)
    {
        cut.Find("button.publish-wizard-next").Click();
        return Task.CompletedTask;
    }

    private static Task FinishAsync(IRenderedComponent<PublishWizardWorkspace> cut)
    {
        cut.Find("button.publish-wizard-finish").Click();
        return Task.CompletedTask;
    }

    /// <summary>Server projections the wizard reads, with no relationship to the retired fixtures.</summary>
    private sealed class StubOperateData : IOperateTransitionDataSource
    {
        private readonly OperateTransitionWorkspace _workspace;

        private StubOperateData(OperateTransitionWorkspace workspace) => _workspace = workspace;

        public static StubOperateData WithFixtures()
        {
            OperateServiceDetail Service(string name, string type, string status, int layers) =>
                new(
                    name,
                    name,
                    type,
                    status,
                    "server",
                    Enumerable.Range(0, layers)
                        .Select(index => new OperateServiceLayerProjection(
                            index,
                            $"layer-{index}",
                            "LineString",
                            $"res-{index}",
                            $"Resource {index}",
                            null))
                        .ToArray(),
                    [],
                    []);

            var connections = new[]
            {
                new OperateConnectionSummary("conn-a", "hydro-primary", "postgis", "hydro.db", "svc", "Connected", "just now", null),
                new OperateConnectionSummary("conn-b", "warehouse", "duckdb", "wh.db", "svc", "Connected", "just now", null),
            };

            return new StubOperateData(new OperateTransitionWorkspace(
                connections,
                [],
                [Service("hydrology-fs", "FeatureServer", "running", 2), Service("basemaps-ms", "MapServer", "running", 1)],
                [],
                []));
        }

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace);

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Connections.FirstOrDefault(c => c.Id == connectionId));

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OperateResourceEditPreview?>(null);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_workspace.Services.FirstOrDefault(s => s.Name == serviceName));
    }

    /// <summary>Records what the wizard actually asked the server to do.</summary>
    private sealed class RecordingPublishOperation(ServiceLayerPublishResult? result = null) : IServiceLayerPublishOperation
    {
        public List<ServiceLayerPublishCommand> Commands { get; } = [];

        public string? LastListedConnectionId { get; private set; }

        public Task<ServiceLayerPublishResult> PublishAsync(
            ServiceLayerPublishCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(result ?? new ServiceLayerPublishResult
            {
                Succeeded = true,
                State = "Published",
                LayerId = 3,
                LayerName = command.LayerName,
                ServiceName = command.ServiceName
            });
        }

        public Task<ServiceProtocolEnableResult> EnableProtocolsAsync(
            string serviceName,
            IReadOnlyList<string> protocols,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServiceProtocolEnableResult { Succeeded = true, State = "Enabled" });

        public Task<IReadOnlyList<ServiceLayerPublishTable>> ListTablesAsync(
            string connectionId,
            CancellationToken cancellationToken = default)
        {
            LastListedConnectionId = connectionId;

            IReadOnlyList<ServiceLayerPublishTable> tables = connectionId == "conn-a"
                ?
                [
                    new ServiceLayerPublishTable
                    {
                        Schema = "hydro",
                        Table = "streams",
                        GeometryColumn = "the_geom",
                        GeometryType = "LineString",
                        Srid = 3857,
                        EstimatedRows = 4096,
                        Columns = ["id", "name", "the_geom"]
                    },
                    new ServiceLayerPublishTable
                    {
                        Schema = "hydro",
                        Table = "basins",
                        GeometryColumn = "geom",
                        GeometryType = "Polygon",
                        Srid = 4326,
                        Columns = ["id", "geom"]
                    }
                ]
                : [];

            return Task.FromResult(tables);
        }
    }
}
