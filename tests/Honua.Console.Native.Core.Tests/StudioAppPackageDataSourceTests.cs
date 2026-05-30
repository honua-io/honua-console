using System.Text.Json;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class StudioAppPackageDataSourceTests
{
    [Fact]
    public void TemplateStartsWithOneHomePageAndNoActions()
    {
        var template = StudioAppPackageMapper.CreateTemplate();

        Assert.Single(template.Pages);
        Assert.Equal("/", template.Pages[0].Route);
        Assert.Empty(template.Actions);
        Assert.False(template.IsExistingDraft);
        Assert.False(template.IsPublished);
    }

    [Fact]
    public void PublishGateBlocksUntilContentBindingPermissionsAndShareReviewAreResolved()
    {
        var state = StudioAppPackageMapper.CreateTemplate();

        // Blank home page is unbound and the share/embed policy is unreviewed.
        var blocked = StudioAppPackageMapper.EvaluatePublishReadiness(state);
        Assert.False(blocked.CanPublish);
        Assert.Contains(blocked.UnmetRequirements, requirement => requirement.Contains("content version", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(blocked.UnmetRequirements, requirement => requirement.Contains("share/embed", StringComparison.OrdinalIgnoreCase));

        state.Pages[0].ContentBinding = "content:permits@v3";
        state.Actions.Add(new StudioAppActionState { Name = "submit", RequiredPermission = string.Empty });

        // An action without a permission requirement still blocks publish (AC: actions declare permissions).
        var missingPermission = StudioAppPackageMapper.EvaluatePublishReadiness(state);
        Assert.False(missingPermission.CanPublish);
        Assert.Contains(missingPermission.UnmetRequirements, requirement => requirement.Contains("permission", StringComparison.OrdinalIgnoreCase));

        state.Actions[0].RequiredPermission = "editor";
        state.ShareEmbedPolicyReviewed = true;

        var ready = StudioAppPackageMapper.EvaluatePublishReadiness(state);
        Assert.True(ready.CanPublish);
        Assert.Empty(ready.UnmetRequirements);
    }

    [Fact]
    public void EnvelopeBodyProjectsPagesNavigationActionsAndSharePolicy()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Field operations";
        state.Pages[0].ContentBinding = "content:permits@v3";
        state.Pages[0].Title = "Home map";
        state.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/", RequiredPermission = "editor" });
        state.Visibility = "organization";
        state.EmbedEnabled = true;
        state.ShareEmbedPolicyReviewed = true;

        var body = StudioAppPackageMapper.BuildEnvelopeBody(state);

        Assert.Equal(StudioAppPackageMapper.SchemaVersion, body.GetProperty("schemaVersion").GetString());
        Assert.Equal("Field operations", body.GetProperty("title").GetString());

        var pages = body.GetProperty("pages");
        Assert.Equal(1, pages.GetArrayLength());
        Assert.Equal("content:permits@v3", pages[0].GetProperty("component").GetProperty("binding").GetString());

        // Navigation is derived from routed pages.
        Assert.Equal(1, body.GetProperty("navigation").GetArrayLength());
        Assert.Equal("Home map", body.GetProperty("navigation")[0].GetProperty("label").GetString());

        var actions = body.GetProperty("actions");
        Assert.Equal(1, actions.GetArrayLength());
        Assert.Equal("editor", actions[0].GetProperty("requiredPermission").GetString());

        var share = body.GetProperty("sharePolicy");
        Assert.Equal("organization", share.GetProperty("visibility").GetString());
        Assert.True(share.GetProperty("embed").GetBoolean());
        Assert.True(share.GetProperty("reviewed").GetBoolean());
    }

    [Fact]
    public async Task UnsupportedDataSourceSurfacesMissingBindingInsteadOfMockData()
    {
        var dataSource = new UnsupportedStudioAppPackageDataSource();

        var load = await dataSource.LoadAsync(null);
        Assert.False(load.HasEditor);
        Assert.Null(load.State);
        var binding = Assert.Single(load.CapabilityStates);
        Assert.Equal("Missing binding", binding.State);
        Assert.Equal("App builder", binding.Surface);

        var save = await dataSource.SaveDraftAsync(StudioAppPackageMapper.CreateTemplate());
        Assert.False(save.Succeeded);
        Assert.NotNull(save.Issue);
        Assert.Equal("Missing binding", save.Issue!.State);

        var publish = await dataSource.PublishAsync(StudioAppPackageMapper.CreateTemplate());
        Assert.False(publish.Succeeded);
        Assert.NotNull(publish.Issue);
    }

    [Fact]
    public void EnvelopeBodyIsValidJsonDocument()
    {
        var body = StudioAppPackageMapper.BuildEnvelopeBody(StudioAppPackageMapper.CreateTemplate());

        // Round-trips through the serializer; guards against an invalid projection shape.
        var json = JsonSerializer.Serialize(body);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}
