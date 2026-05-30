using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the side-effect-free map pre-publish evaluator and the unsupported
/// (missing-binding) map data source. No server or in-memory map client is involved.
/// </summary>
public sealed class StudioMapPublishEvaluatorTests
{
    [Fact]
    public void Evaluate_CompleteMap_IsPublishReady()
    {
        var state = new StudioMapEditorState
        {
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "-158.3,21.2,-157.6,21.7"
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12" });

        var readiness = StudioMapPublishEvaluator.Evaluate(state);

        Assert.True(readiness.CanPublish);
        Assert.Empty(readiness.UnmetRequirements);
    }

    [Fact]
    public void Evaluate_EmptyDraft_GatesOnEveryRequirement()
    {
        var readiness = StudioMapPublishEvaluator.Evaluate(new StudioMapEditorState());

        Assert.False(readiness.CanPublish);
        Assert.Contains("Give the map a title.", readiness.UnmetRequirements);
        Assert.Contains("Add at least one layer.", readiness.UnmetRequirements);
        Assert.Contains("Select a basemap.", readiness.UnmetRequirements);
        Assert.Contains("Set the initial extent.", readiness.UnmetRequirements);
    }

    [Fact]
    public void Evaluate_LayerWithoutSourceRef_GatesPublish()
    {
        var state = new StudioMapEditorState
        {
            Title = "Public works",
            Basemap = "basemap:streets",
            InitialExtent = "0,0,1,1"
        };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = string.Empty });

        var readiness = StudioMapPublishEvaluator.Evaluate(state);

        Assert.False(readiness.CanPublish);
        Assert.Contains("Every layer must bind a source reference.", readiness.UnmetRequirements);
    }

    [Fact]
    public async Task UnsupportedDataSource_RendersMissingBindingEverywhere()
    {
        var source = new UnsupportedStudioMapPackageDataSource();

        var workspace = await source.GetWorkspaceAsync();
        Assert.Empty(workspace.Packages);
        Assert.Single(workspace.CapabilityStates);
        Assert.Equal("Missing binding", workspace.CapabilityStates[0].State);

        var load = await source.LoadAsync(null);
        Assert.Null(load.State);
        Assert.Single(load.CapabilityStates);

        var save = await source.SaveDraftAsync(new StudioMapEditorState());
        Assert.False(save.Succeeded);
        Assert.NotNull(save.Issue);

        var publish = await source.PublishAsync(new StudioMapEditorState());
        Assert.False(publish.Succeeded);

        var reopen = await source.ReopenAsync(new StudioMapEditorState { MapId = "map-1", Version = 1 });
        Assert.False(reopen.Succeeded);
    }
}
