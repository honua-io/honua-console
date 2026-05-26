using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// The runtime default when no honua-server is configured must render the shared blocked surface for the
/// workflow editor - never seeded workflow data (Console Patterns Charter §11).
/// </summary>
public sealed class UnsupportedStudioWorkflowPackageClientTests
{
    [Fact]
    public async Task OpenEditor_ReturnsMissingBindingAndNoSeededData()
    {
        var client = new UnsupportedStudioWorkflowPackageClient();

        var context = await client.OpenEditorAsync("new");

        Assert.NotNull(context.BindingState);
        Assert.Equal("Missing binding", context.BindingState!.State);
        Assert.Contains("#1185", context.BindingState.Detail, StringComparison.Ordinal);
        Assert.Null(context.Draft);
        Assert.Empty(context.NodeDefinitions);
    }

    [Fact]
    public async Task SaveDryRunPublish_AllCarryMissingBinding()
    {
        var client = new UnsupportedStudioWorkflowPackageClient();
        var draft = new Honua.Console.Shell.Models.StudioWorkflowPackageDraft();

        var save = await client.SaveVersionAsync(draft, "note");
        var dryRun = await client.DryRunAsync(draft);
        var publish = await client.PublishAsync(draft);

        Assert.NotNull(save.BindingState);
        Assert.NotNull(dryRun.BindingState);
        Assert.NotNull(publish.BindingState);
        Assert.Equal("blocked", dryRun.Status);
        Assert.Equal("blocked", publish.Status);
    }

    [Fact]
    public async Task ListAndGet_ReturnEmptyOrNull_NeverSeeded()
    {
        var client = new UnsupportedStudioWorkflowPackageClient();

        Assert.Empty(await client.ListDraftsAsync());
        Assert.Empty(await client.ListNodeDefinitionsAsync());
        Assert.Null(await client.GetDraftAsync("any"));
        Assert.Null(await client.GetJobEvidenceAsync("any"));
    }
}
