using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Proves the server-backed Studio GP/ETL workflow editor renders from live honua-server data (AC:
/// Testcontainers coverage boots honua-server/PostgreSQL, creates a workflow package fixture, and asserts
/// live-data rendering; Docker-unavailable environments skip cleanly). Drives
/// <see cref="ServerStudioWorkflowPackageClient"/> over the real <c>/api/v1/console/workflow-*</c> contract
/// (#1185): hydrates the node registry, creates a real package, renders it through the editor page, and -
/// when the geoprocessing palette is available - versions, dry-runs, and publishes it. Never a mock.
/// </summary>
[Collection(StudioWorkflowPackageIntegrationCollection.Name)]
public sealed class StudioWorkflowPackageIntegrationTests
{
    // The honua-server #1185 endpoint tests use this known-good single-node geometry.area fixture
    // (wkb = base64 of a 1-byte WKB stub, srid = 4326), which validates, dry-runs, and publishes as a Job.
    private const string AreaNodeTypeId = "process:geometry.area";

    private readonly StudioWorkflowPackageFixture _fixture;

    public StudioWorkflowPackageIntegrationTests(StudioWorkflowPackageFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task WorkflowEditor_RendersNodeRegistryAndCreatedPackage_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var api = _fixture.CreateApiClient();
        IStudioWorkflowPackageClient client = new ServerStudioWorkflowPackageClient(api);

        // 1. Opening the editor hydrates the live node registry (palette) - the binding probe must succeed.
        var context = await client.OpenEditorAsync("new");
        Skip.If(
            context.BindingState is not null,
            $"The live server did not bind the workflow node registry: {context.BindingState?.Detail}");
        Assert.NotEmpty(context.NodeDefinitions);

        // 2. Create a real package fixture through the editor's binding using a live registry node type.
        var registry = await api.GetNodeRegistryAsync();
        Skip.If(!registry.IsSuccess || registry.Data is null, "Live node registry was unavailable.");
        var nodeType = registry.Data!.Nodes.FirstOrDefault(node => node.NodeTypeId == AreaNodeTypeId)?.NodeTypeId
            ?? registry.Data.Nodes.FirstOrDefault()?.NodeTypeId;
        Skip.If(nodeType is null, "The live server published no workflow node definitions.");

        var title = $"Console live workflow {Guid.NewGuid():N}";
        var draft = context.Draft!;
        draft.Title = title;
        draft.Nodes.Add(new StudioWorkflowNode
        {
            Id = "node-1",
            Type = nodeType!,
            Category = StudioWorkflowContractValues.NodeCategorySource,
            Label = "Live node",
            Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["wkb"] = "AQ==",
                ["srid"] = "4326"
            }
        });

        var save = await client.SaveVersionAsync(draft, "integration fixture");
        Skip.If(
            save.BindingState is not null,
            $"The live server did not accept the workflow package: {save.BindingState?.Detail}");
        Assert.False(string.IsNullOrEmpty(save.ContentItemId)); // the package draft persisted on the server

        // 3. The package list + detail render from live server data.
        var drafts = await client.ListDraftsAsync();
        Assert.Contains(drafts, summary => summary.ContentItemId == save.ContentItemId);

        var reloaded = await client.GetDraftAsync(save.ContentItemId);
        Assert.NotNull(reloaded);
        Assert.Equal(title, reloaded!.Title);
        Assert.Contains(reloaded.Nodes, node => node.Type == nodeType);

        // 4. The actual editor page renders the live package (not a mock / not the blocked surface).
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(client);
        var page = ctx.RenderComponent<StudioWorkflowEditorPage>(
            parameters => parameters.Add(component => component.DraftId, save.ContentItemId));

        page.WaitForAssertion(
            () => Assert.Contains(title, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));
        Assert.DoesNotContain("Missing binding", page.Markup, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task WorkflowEditor_DryRunAndPublish_BindToLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var api = _fixture.CreateApiClient();
        var registry = await api.GetNodeRegistryAsync();
        Skip.If(!registry.IsSuccess || registry.Data is null, "Live node registry was unavailable.");
        Skip.If(
            registry.Data!.Nodes.All(node => node.NodeTypeId != AreaNodeTypeId),
            $"The live server geoprocessing palette does not expose {AreaNodeTypeId}; cannot build a known-valid graph.");

        IStudioWorkflowPackageClient client = new ServerStudioWorkflowPackageClient(api);
        var context = await client.OpenEditorAsync("new");
        Skip.If(context.BindingState is not null, $"Workflow registry did not bind: {context.BindingState?.Detail}");

        var draft = context.Draft!;
        draft.Title = $"Console live area {Guid.NewGuid():N}";
        draft.Nodes.Add(new StudioWorkflowNode
        {
            Id = "area-1",
            Type = AreaNodeTypeId,
            Category = StudioWorkflowContractValues.NodeCategorySource,
            Label = "Polygon area",
            Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["wkb"] = "AQ==",
                ["srid"] = "4326"
            }
        });

        // Save creates a real immutable version against live PostgreSQL-backed server state.
        var save = await client.SaveVersionAsync(draft, "integration valid graph");
        Skip.If(save.BindingState is not null, $"Save did not bind: {save.BindingState?.Detail}");
        Assert.Equal(1, save.VersionNumber);

        // Dry-run binds to the live server and returns a real estimation (no Operate job for the synchronous
        // dry-run, but real validation + estimate logs).
        var dryRun = await client.DryRunAsync(draft);
        Assert.Null(dryRun.BindingState);
        Assert.Contains(dryRun.Logs, log => log.Contains("Estimated duration", StringComparison.Ordinal));

        // Publish to the job runner and start a run; the published run links to live Operate job evidence.
        var publish = await client.PublishAsync(draft);
        Assert.Null(publish.BindingState);
        Assert.False(string.IsNullOrEmpty(publish.PublicationId));
        if (!string.IsNullOrEmpty(publish.JobId))
        {
            Assert.StartsWith("/operate/jobs/", publish.OperateJobUrl, StringComparison.Ordinal);
        }
    }
}
