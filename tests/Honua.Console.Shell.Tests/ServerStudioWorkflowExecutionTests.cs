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
        draft.ApplyRetryToAllNodes();
        await client.SaveVersionAsync(draft, "execution");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Assert.Single(api.Packages).Graph));
        Assert.Equal(2, json.RootElement.GetProperty("nodes").GetArrayLength());
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
            FromNodeId = draft.Nodes[0].Id,
            ToNodeId = draft.Nodes[1].Id,
            Kind = StudioWorkflowContractValues.EdgeKindFailure
        });
        await client.SaveVersionAsync(draft, "failure edge");
        var edge = Assert.Single(Assert.Single(api.Packages).Graph.Edges);
        Assert.Equal(WorkflowEdgeKind.Failure, edge.Kind);
        Assert.Null(edge.TargetPort);
        Assert.Null(edge.SourcePort);
        var reloaded = (await client.GetDraftAsync(draft.ContentItemId))!;
        Assert.Equal(StudioWorkflowContractValues.EdgeKindFailure, Assert.Single(reloaded.Edges).Kind);
    }

    [Fact]
    public async Task SaveVersion_TypedExecutionSettings_RoundTripThenApplyChangedDefaults()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        api.Packages.Add(new WorkflowPackage
        {
            PackageId = "imported",
            Name = "Imported",
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
        Assert.Equal("source", saved[1].Condition!.SourceStepId);
        Assert.Equal(WorkflowStepConditionKind.HasArtifact, saved[1].Condition!.Kind);
        Assert.Equal(2, saved[1].Condition!.Threshold);
        Assert.Equal("roads", saved[1].Condition!.ArtifactLabel);
        Assert.True(saved[1].Condition!.Negate);
        Assert.Equal(new[] { "east", "west" }, saved[1].ForEach!.Items);
        Assert.Equal("${item}", saved[1].ForEach!.ItemPlaceholder);
        Assert.Equal(2, saved[1].ForEach!.MaxIterations);

        var reloaded = (await client.GetDraftAsync("imported"))!;
        reloaded.RetryPolicy.MaxAttempts = 5;
        reloaded.ApplyRetryToAllNodes();
        await client.SaveVersionAsync(reloaded, "updated defaults");
        Assert.All(Assert.Single(api.Packages).Graph.Nodes, node => Assert.Equal(5, node.RetryPolicy!.MaxAttempts));
        reloaded.Nodes[0].RetryPolicy = new StepRetryPolicy { MaxAttempts = 8, InitialDelaySeconds = 3 };
        await client.SaveVersionAsync(reloaded, "same defaults, changed node");
        Assert.Equal(8, api.Packages[0].Graph.Nodes[0].RetryPolicy!.MaxAttempts);
        Assert.Equal(10, api.Packages[0].Graph.Nodes[1].TimeoutSeconds);
        Assert.Equal(WorkflowStepFailurePolicy.Skip, api.Packages[0].Graph.Nodes[1].FailurePolicy);
    }

    [Fact]
    public async Task SaveVersion_LegacyRetryMetadata_MigratesToExecutableFields()
    {
        var api = new FakeWorkflowApi();
        api.Packages.Add(new WorkflowPackage
        {
            PackageId = "legacy",
            Name = "Legacy",
            Graph = new WorkflowGraph
            {
                Nodes = [new WorkflowNode { NodeId = "work", NodeTypeId = "process:test.echo" }],
                EditorMetadata = new Dictionary<string, string>
                {
                    [ServerStudioWorkflowPackageClient.EditorStateKey] = """{"retryPolicy":{"maxAttempts":7,"backoffSeconds":8,"failureMode":"route-failure-edges"},"parameters":[{"name":"county","type":"string","required":true}],"publicationIntent":{"mode":"scheduled-job","routeSlug":"nightly"}}"""
                }
            }
        });
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = (await client.GetDraftAsync("legacy"))!;
        await client.SaveVersionAsync(draft, "migrate retry settings");
        Assert.Equal(7, Assert.Single(api.Packages).Graph.Nodes[0].RetryPolicy!.MaxAttempts);
        Assert.Equal(8, api.Packages[0].Graph.Nodes[0].RetryPolicy!.InitialDelaySeconds);
        Assert.False(api.Packages[0].Graph.EditorMetadata.ContainsKey(ServerStudioWorkflowPackageClient.EditorStateKey));
        Assert.True(api.Packages[0].Graph.EditorMetadata.ContainsKey(ServerStudioWorkflowPackageClient.AuthoringStateKey));
        var reloaded = (await client.GetDraftAsync("legacy"))!;
        Assert.Equal("county", Assert.Single(reloaded.Parameters).Name);
        Assert.True(reloaded.Parameters[0].Required);
        Assert.Equal("scheduled-job", reloaded.PublicationIntent.Mode);
        Assert.Equal("nightly", reloaded.PublicationIntent.RouteSlug);
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
        Assert.Null(published.BindingState);
        Assert.Contains(published.ValidationIssues, issue => issue.Scope == "retryPolicy.failureMode");
        Assert.Empty(api.Packages);
        Assert.Empty(api.Publications);
        var dryRun = await client.DryRunAsync(draft);
        Assert.Equal("blocked", dryRun.Status);
        Assert.Null(dryRun.BindingState);
        Assert.Contains(dryRun.ValidationIssues, issue => issue.Scope == "retryPolicy.failureMode");
        Assert.Empty(api.Packages);
    }

    [Fact]
    public async Task SaveVersion_FailFastWithoutFailureEdges_PreservesAuthoringChoiceOnReload()
    {
        var api = new FakeWorkflowApi();
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        draft.RetryPolicy.FailureMode = "fail-fast";
        var saved = await client.SaveVersionAsync(draft, "fail-fast");
        Assert.Empty(saved.ValidationIssues);
        var reloaded = (await client.GetDraftAsync(draft.ContentItemId))!;
        Assert.Equal("fail-fast", reloaded.RetryPolicy.FailureMode);
    }

    [Fact]
    public async Task SaveVersion_MixedRetryPolicies_PreservesAbsentPolicy()
    {
        var api = new FakeWorkflowApi();
        api.Packages.Add(new WorkflowPackage
        {
            PackageId = "mixed",
            Name = "Mixed",
            Graph = new WorkflowGraph
            {
                Nodes = [new WorkflowNode { NodeId = "source", NodeTypeId = "process:test.echo", RetryPolicy = new StepRetryPolicy { MaxAttempts = 3, InitialDelaySeconds = 2 } },
                    new WorkflowNode { NodeId = "sink", NodeTypeId = "process:test.echo" }]
            }
        });
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = (await client.GetDraftAsync("mixed"))!;
        await client.SaveVersionAsync(draft, "no retry edit");
        Assert.Null(Assert.Single(api.Packages).Graph.Nodes[1].RetryPolicy);
    }

    [Fact]
    public async Task Publish_ServerRejectsNewExecutionFields_ShowsValidationOnBoundSurface()
    {
        var api = new FakeWorkflowApi { VersionStatusCode = 400 };
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        var result = await client.PublishAsync(draft);
        Assert.Equal("blocked", result.Status);
        Assert.Null(result.BindingState);
        Assert.Contains(result.ValidationIssues, issue => issue.Scope == "graph");
    }

    [Fact]
    public async Task DryRun_ServerRejectsExecutionFields_ReturnsValidationWithoutBindingFailure()
    {
        var api = new FakeWorkflowApi { VersionStatusCode = 400 };
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        var result = await client.DryRunAsync(draft);
        Assert.Equal("blocked", result.Status);
        Assert.Null(result.BindingState);
        Assert.Contains(result.ValidationIssues, issue => issue.Scope == "graph");
        Assert.NotEmpty(result.Logs);
    }

    [Fact]
    public async Task DryRun_CreatedVersionCarriesFailures_RepeatedCallsUseSameServerValidation()
    {
        var api = new FakeWorkflowApi
        {
            VersionValidation = new WorkflowPackageValidationResult
            {
                IsValid = false,
                Failures = [new WorkflowPackageValidationFailure { Code = "INVALID_TIMEOUT", FieldPath = "graph.nodes[work].timeoutSeconds", Message = "Timeout must be positive." }]
            }
        };
        var client = new ServerStudioWorkflowPackageClient(api);
        var draft = await NewDraftWithSinkAsync(client);
        var first = await client.DryRunAsync(draft);
        var second = await client.DryRunAsync(draft);
        Assert.Equal(2, api.DryRunCalls);
        Assert.Equal("blocked", first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Null(first.BindingState);
        Assert.Null(second.BindingState);
        Assert.Contains(first.Logs, log => log.Contains("Timeout must be positive.", StringComparison.Ordinal));
        Assert.Equal(first.Logs, second.Logs);
        Assert.Equal("graph.nodes[work].timeoutSeconds", Assert.Single(first.ValidationIssues).Scope);
        Assert.Equal(first.ValidationIssues[0].Message, Assert.Single(second.ValidationIssues).Message);
    }

}
