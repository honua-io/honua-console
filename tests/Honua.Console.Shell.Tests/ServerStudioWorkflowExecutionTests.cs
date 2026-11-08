using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed partial class ServerStudioWorkflowPackageClientTests
{
    [Fact]
    public async Task SaveVersion_RetrySettings_AreExecutableNodeFields()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.RetryPolicy.MaxAttempts = 3;
        draft.RetryPolicy.BackoffSeconds = 7;
        await client.SaveVersionAsync(draft, "execution");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(api.Packages).Graph));
        foreach (var node in json.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(3, node.GetProperty("retryPolicy").GetProperty("maxAttempts").GetInt32());
            Assert.Equal(7, node.GetProperty("retryPolicy").GetProperty("initialDelaySeconds").GetInt32());
            Assert.Equal("Fail", node.GetProperty("failurePolicy").GetString());
        }
        Assert.False(json.RootElement.GetProperty("editorMetadata").TryGetProperty(ServerStudioWorkflowPackageClient.EditorStateKey, out _));
        var state = json.RootElement.GetProperty("editorMetadata").GetProperty(ServerStudioWorkflowPackageClient.AuthoringStateKey).GetString();
        Assert.DoesNotContain("retryPolicy", state!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveVersion_FailureEdges_AreControlOnly()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.Edges.Add(new StudioWorkflowEdge
        {
            FromNodeId = draft.Nodes[0].Id, ToNodeId = draft.Nodes[1].Id, Kind = StudioWorkflowContractValues.EdgeKindFailure
        });
        await client.SaveVersionAsync(draft, "failure edge");
        var edge = Assert.Single(Assert.Single(api.Packages).Graph.Edges);
        Assert.Equal(WorkflowEdgeKind.Failure, edge.Kind);
        Assert.Null(edge.TargetPort);
    }
    [Fact]
    public async Task SaveVersion_TypedExecutionSettings_RoundTripThenApplyChangedDefaults()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        api.Packages.Add(new WorkflowPackage
        {
            PackageId = "imported", Name = "Imported",
            Graph = JsonSerializer.Deserialize<WorkflowGraph>("""
                {"nodes":[{"nodeId":"source","nodeTypeId":"process:test.echo",
                "retryPolicy":{"maxAttempts":3,"initialDelaySeconds":2,"backoffMultiplier":2,"maxDelaySeconds":30}},
                {"nodeId":"work","nodeTypeId":"process:test.echo","failurePolicy":"Skip","timeoutSeconds":9,
                "condition":{"sourceStepId":"source","kind":"HasArtifact","artifactLabel":"roads","threshold":2,"negate":true},
                "forEach":{"items":["east","west"],"maxIterations":2}}]}
                """)!
        });
        var draft = (await client.GetDraftAsync("imported"))!;
        var before = StudioWorkflowPackageDraftContent.CreateFingerprint(draft);
        draft.Nodes[1].TimeoutSeconds = 10;
        Assert.NotEqual(before, StudioWorkflowPackageDraftContent.CreateFingerprint(draft));
        await client.SaveVersionAsync(draft, "typed settings");
        var saved = Assert.Single(api.Packages).Graph.Nodes;
        Assert.Equal(2.0, saved[0].RetryPolicy!.BackoffMultiplier);
        Assert.Equal(30, saved[0].RetryPolicy!.MaxDelaySeconds);
        Assert.Equal(WorkflowStepFailurePolicy.Skip, saved[1].FailurePolicy);
        Assert.Equal(10, saved[1].TimeoutSeconds);
        Assert.Equal("roads", saved[1].Condition!.ArtifactLabel);
        Assert.True(saved[1].Condition!.Negate);
        Assert.Equal(new[] { "east", "west" }, saved[1].ForEach!.Items);

        var reloaded = (await client.GetDraftAsync("imported"))!;
        reloaded.RetryPolicy.MaxAttempts = 5;
        await client.SaveVersionAsync(reloaded, "updated defaults");
        await client.SaveVersionAsync(reloaded, "same defaults");
        Assert.All(Assert.Single(api.Packages).Graph.Nodes, node => Assert.Equal(5, node.RetryPolicy!.MaxAttempts));
        Assert.Equal(10, api.Packages[0].Graph.Nodes[1].TimeoutSeconds);
    }

    [Fact]
    public async Task SaveVersion_LegacyRetryMetadata_MigratesToExecutableFields()
    {
        var api = new FakeWorkflowApi();
        api.Packages.Add(new WorkflowPackage
        {
            PackageId = "legacy", Name = "Legacy", Graph = new WorkflowGraph
            {
                Nodes = [new WorkflowNode { NodeId = "work", NodeTypeId = "process:test.echo" }],
                EditorMetadata = new Dictionary<string, string>
                {
                    [ServerStudioWorkflowPackageClient.EditorStateKey] = """{"retryPolicy":{"maxAttempts":7,"backoffSeconds":8,"failureMode":"route-failure-edges"}}"""
                }
            }
        });
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = (await client.GetDraftAsync("legacy"))!;
        await client.SaveVersionAsync(draft, "migrate retry settings");
        Assert.Equal(7, Assert.Single(api.Packages).Graph.Nodes[0].RetryPolicy!.MaxAttempts);
        Assert.Equal(8, api.Packages[0].Graph.Nodes[0].RetryPolicy!.InitialDelaySeconds);
        Assert.False(api.Packages[0].Graph.EditorMetadata.ContainsKey(ServerStudioWorkflowPackageClient.EditorStateKey));
    }

    [Theory]
    [InlineData("quarantine-artifacts")]
    [InlineData("unknown-policy")]
    [InlineData("fail-fast")]
    public async Task SaveAndPublish_UnsupportedFailureMode_ShowsValidationWithoutCallingServer(string mode)
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.RetryPolicy.FailureMode = mode;
        draft.Edges.Add(new StudioWorkflowEdge { Kind = StudioWorkflowContractValues.EdgeKindFailure });
        var saved = await client.SaveVersionAsync(draft, "unsupported");
        Assert.Contains(saved.ValidationIssues, issue => issue.Scope == "retryPolicy.failureMode");
        var published = await client.PublishAsync(draft);
        Assert.Equal("blocked", published.Status);
        Assert.NotNull(published.BindingState);
        Assert.Empty(api.Packages);
        Assert.Empty(api.Publications);
    }

}
