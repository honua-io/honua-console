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

    [Fact]
    public void ApplyEnvelopeBodyRoundTripsAuthoredState()
    {
        var authored = StudioAppPackageMapper.CreateTemplate();
        authored.Title = "Field operations";
        authored.Summary = "Permit inspections";
        authored.Pages[0].Title = "Permits map";
        authored.Pages[0].ComponentKind = "dashboard";
        authored.Pages[0].ContentBinding = "content:permits@v3";
        authored.Pages.Add(new StudioAppPageState { Route = "/form", Title = "Inspect", ComponentKind = "form", ContentBinding = "content:inspect@v1" });
        authored.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/form", RequiredPermission = "operator" });
        authored.Visibility = "organization";
        authored.EmbedEnabled = true;
        authored.ShareEmbedPolicyReviewed = true;

        var body = StudioAppPackageMapper.BuildEnvelopeBody(authored);

        // Rehydrating a fresh scaffold from the projected body must reconstruct the authored content so a
        // reopened/reloaded draft renders real data, not a blank template.
        var rehydrated = StudioAppPackageMapper.CreateTemplate();
        StudioAppPackageMapper.ApplyEnvelopeBody(rehydrated, body);

        Assert.Equal("Field operations", rehydrated.Title);
        Assert.Equal("Permit inspections", rehydrated.Summary);
        Assert.Equal(2, rehydrated.Pages.Count);
        Assert.Equal("dashboard", rehydrated.Pages[0].ComponentKind);
        Assert.Equal("content:permits@v3", rehydrated.Pages[0].ContentBinding);
        Assert.Equal("/form", rehydrated.Pages[1].Route);
        var action = Assert.Single(rehydrated.Actions);
        Assert.Equal("operator", action.RequiredPermission);
        Assert.Equal("organization", rehydrated.Visibility);
        Assert.True(rehydrated.EmbedEnabled);
        Assert.True(rehydrated.ShareEmbedPolicyReviewed);
    }

    [Fact]
    public void ApplyEnvelopeBodyToleratesNullAndMalformedBody()
    {
        var state = StudioAppPackageMapper.CreateTemplate();
        state.Title = "Keep me";

        StudioAppPackageMapper.ApplyEnvelopeBody(state, null);
        // A non-object body (e.g. an array) must be ignored rather than throwing.
        StudioAppPackageMapper.ApplyEnvelopeBody(state, JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }));

        Assert.Equal("Keep me", state.Title);
        Assert.Single(state.Pages);
    }

    [Fact]
    public async Task UnsupportedDataSourceSurfacesMissingBindingForHistoryReopenAndRollback()
    {
        var dataSource = new UnsupportedStudioAppPackageDataSource();
        var itemId = Guid.NewGuid();

        var history = await dataSource.LoadVersionHistoryAsync(itemId);
        Assert.False(history.HasVersions);
        Assert.NotNull(history.Issue);
        Assert.Equal("Missing binding", history.Issue!.State);

        var reopen = await dataSource.ReopenAsync(itemId, Guid.NewGuid());
        Assert.False(reopen.Succeeded);
        Assert.Equal("Missing binding", reopen.Issue!.State);

        var rollback = await dataSource.RollbackAsync(itemId, Guid.NewGuid(), reason: null);
        Assert.False(rollback.Succeeded);

        var preview = await dataSource.PreviewAsync(StudioAppPackageMapper.CreateTemplate());
        Assert.False(preview.Succeeded);
    }
}
