using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for <see cref="StudioWorkflowGraph"/> — the WORKFLOW Studio family's
/// FINAL visible output: the generated workflow.package rendered as a layered node/edge graph (DAG).
/// Asserts that the REAL generated nodes (type + label) and the edges/flow between them are rendered,
/// that the layout layers downstream nodes after their dependencies, and that an empty workflow shows
/// an honest empty state instead of fabricated steps (Charter §11). Renders fully without any server or
/// JS runtime bound.
/// </summary>
public sealed class StudioWorkflowGraphRenderTests
{
    private static StudioWorkflowNode Node(string id, string type, string category, string label) =>
        new() { Id = id, Type = type, Category = category, Label = label };

    private static StudioWorkflowEdge Edge(string from, string to, string kind = StudioWorkflowContractValues.EdgeKindSuccess) =>
        new() { Id = $"{from}->{to}", FromNodeId = from, ToNodeId = to, Kind = kind };

    [Fact]
    public void WorkflowGraph_WithConnectedNodes_RendersEveryNodeLabelAndEdgeFlow()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        // A small, real connected workflow: trigger → read → transform → publish, with a failure route
        // off the transform to a notify sink.
        var nodes = new List<StudioWorkflowNode>
        {
            Node("n-trigger", "schedule.trigger", StudioWorkflowContractValues.NodeCategorySource, "Nightly trigger"),
            Node("n-read", "source.read", StudioWorkflowContractValues.NodeCategorySource, "Read parcels"),
            Node("n-transform", "transform.sql", StudioWorkflowContractValues.NodeCategoryTransform, "Normalize fields"),
            Node("n-publish", "sink.publish", StudioWorkflowContractValues.NodeCategorySink, "Publish layer"),
            Node("n-notify", "sink.failure-notify", StudioWorkflowContractValues.NodeCategorySink, "Notify on failure"),
        };
        var edges = new List<StudioWorkflowEdge>
        {
            Edge("n-trigger", "n-read"),
            Edge("n-read", "n-transform"),
            Edge("n-transform", "n-publish"),
            Edge("n-transform", "n-notify", StudioWorkflowContractValues.EdgeKindFailure),
        };

        var cut = ctx.Render<StudioWorkflowGraph>(parameters => parameters
            .Add(p => p.Nodes, nodes)
            .Add(p => p.Edges, edges));

        // The graph is rendered (not the empty state) and contains a node box per real node, each with its
        // label and type.
        Assert.Contains("data-workflow-graph=\"true\"", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-workflow-graph-empty", cut.Markup, StringComparison.Ordinal);

        Assert.Equal(5, cut.FindAll(".studio-workflow-graph-node").Count);
        foreach (var node in nodes)
        {
            Assert.Contains(node.Label, cut.Markup, StringComparison.Ordinal);
            Assert.Contains(node.Type, cut.Markup, StringComparison.Ordinal);
        }

        // Every edge is rendered in the connection list as from-label → to-label.
        Assert.Equal(4, cut.FindAll(".studio-workflow-graph-edge-row").Count);
        Assert.Contains("Nightly trigger → Read parcels", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Normalize fields → Publish layer", cut.Markup, StringComparison.Ordinal);

        // The failure route is rendered distinctly.
        Assert.Contains("studio-workflow-graph-edge-failure", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-workflow-graph-node-failure", cut.Markup, StringComparison.Ordinal);

        // The layered flow puts at least one → edge between layers (downstream nodes after their deps).
        Assert.Contains("studio-workflow-graph-edge", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("→", cut.Markup, StringComparison.Ordinal);

        // Layout is derived from the graph: the transform depends on the read, so it must land in a later
        // layer than the read node. Find the layer (column) index of each.
        var layers = cut.FindAll(".studio-workflow-graph-layer");
        Assert.True(layers.Count >= 2, "Expected the connected workflow to lay out across multiple layers.");

        int LayerOf(string nodeId)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].QuerySelector($"[data-workflow-graph-node=\"{nodeId}\"]") is not null)
                {
                    return i;
                }
            }

            return -1;
        }

        Assert.True(LayerOf("n-read") >= 0 && LayerOf("n-transform") >= 0);
        Assert.True(LayerOf("n-transform") > LayerOf("n-read"),
            "Transform depends on read, so it must render in a later layer.");
        Assert.True(LayerOf("n-publish") > LayerOf("n-transform"),
            "Publish depends on transform, so it must render in a later layer.");

        // The dependency annotation on a downstream node names its upstream node.
        var transformDeps = cut.Find("[data-workflow-graph-deps=\"n-transform\"]");
        Assert.Contains("Read parcels", transformDeps.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowGraph_WithNoNodes_RendersHonestEmptyState()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = ctx.Render<StudioWorkflowGraph>(parameters => parameters
            .Add(p => p.Nodes, new List<StudioWorkflowNode>())
            .Add(p => p.Edges, new List<StudioWorkflowEdge>())
            .Add(p => p.EmptyMessage, "No steps yet — describe the pipeline to generate one."));

        // Empty state shows, and no fabricated node boxes are rendered (Charter §11).
        Assert.Contains("data-workflow-graph-empty=\"true\"", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("No steps yet", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".studio-workflow-graph-node"));
        Assert.Empty(cut.FindAll(".studio-workflow-graph-edge-row"));
    }

    [Fact]
    public void WorkflowGraph_WithNullNodes_RendersDefaultEmptyState()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        // No parameters bound at all — the component must not throw and must show the empty state.
        var cut = ctx.Render<StudioWorkflowGraph>();

        Assert.Contains("data-workflow-graph-empty=\"true\"", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".studio-workflow-graph-node"));
    }
}
