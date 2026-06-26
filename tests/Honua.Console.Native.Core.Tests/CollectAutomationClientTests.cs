using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free coverage for <see cref="ICollectAutomationClient"/> (honua-console#219). The seeded in-memory
/// client (a test/demo scaffold, never the merged data source) must enforce the version-lifecycle invariants
/// a live server/Collect projection has to preserve: monotonic versions, validation-gated commits, and
/// append-only restore. The merged runtime default <see cref="UnsupportedCollectAutomationClient"/> must
/// instead surface a missing-binding state on every operation rather than fabricate automation data.
/// </summary>
public sealed class CollectAutomationClientTests
{
    [Fact]
    public async Task SeededAutomationCarriesTriggerBoundRulesAndEngineActions()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();

        var context = await client.OpenEditorAsync(InMemoryCollectAutomationClient.SeedDraftId);

        Assert.Null(context.BindingState);
        var draft = Assert.IsType<CollectAutomationDraft>(context.Draft);
        Assert.Equal(CollectAutomationContractValues.PackageType, draft.PackageType);
        Assert.Equal(CollectAutomationContractValues.SchemaVersion, draft.SchemaVersion);
        Assert.False(string.IsNullOrWhiteSpace(draft.FormId));
        Assert.NotEmpty(draft.Rules);
        Assert.Contains(draft.Rules, rule => rule.Trigger == CollectAutomationContractValues.TriggerFieldChange);
        Assert.Contains(draft.Rules, rule =>
            rule.Actions.Any(action => action.Kind == CollectAutomationContractValues.ActionCompute));
    }

    [Fact]
    public async Task OpenNewDraftSeedsAnEditableStarterRuleWithoutAVersion()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();

        var context = await client.OpenEditorAsync("new");

        Assert.Null(context.BindingState);
        var draft = Assert.IsType<CollectAutomationDraft>(context.Draft);
        Assert.Empty(draft.ContentItemId);
        Assert.Equal(0, draft.VersionNumber);
        Assert.NotEmpty(draft.Rules);
        Assert.NotEmpty(draft.Rules[0].Actions);
    }

    [Fact]
    public async Task SaveCommitsVersionAndMirrorsServerIdentityBackOntoDraft()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync("new")).Draft!;
        draft.Name = "Inspection routing";
        draft.FormId = "form-inspection";

        var save = await client.SaveVersionAsync(draft, "first save");

        Assert.Null(save.BindingState);
        Assert.False(string.IsNullOrWhiteSpace(save.VersionId));
        Assert.Equal(1, save.VersionNumber);
        Assert.Empty(save.ValidationIssues);
        // Server-assigned identity flows back onto the caller's draft so a follow-on save is monotonic.
        Assert.Equal(save.ContentItemId, draft.ContentItemId);
        Assert.Equal(save.VersionId, draft.CurrentVersionId);
        Assert.Equal(1, draft.VersionNumber);
    }

    [Fact]
    public async Task SaveOfInvalidBodyCommitsNoVersionAndReturnsIssues()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync("new")).Draft!;
        draft.Name = "No form bound";
        draft.FormId = string.Empty; // invalid: must bind a form.

        var save = await client.SaveVersionAsync(draft, "invalid save");

        Assert.Null(save.BindingState);
        Assert.Empty(save.VersionId);
        Assert.Contains(save.ValidationIssues, issue =>
            issue.Severity == "error" && issue.Scope == "binding");
    }

    [Fact]
    public async Task RepeatedSavesAreMonotonicAndAppendOnly()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync("new")).Draft!;
        draft.Name = "Escalation routing";
        draft.FormId = "form-escalation";

        var first = await client.SaveVersionAsync(draft, "v1");
        var second = await client.SaveVersionAsync(draft, "v2");
        var third = await client.SaveVersionAsync(draft, "v3");

        Assert.True(second.VersionNumber > first.VersionNumber);
        Assert.True(third.VersionNumber > second.VersionNumber);

        var history = await client.ListVersionsAsync(first.ContentItemId);
        Assert.Null(history.BindingState);
        Assert.Equal(3, history.Versions.Count);
        // Newest first, exactly one current.
        Assert.Equal(third.VersionNumber, history.Versions[0].VersionNumber);
        Assert.Single(history.Versions, version => version.IsCurrent);
    }

    [Fact]
    public async Task RestoreCommitsANewVersionAndNeverMutatesPriorHistory()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync("new")).Draft!;
        draft.Name = "Versioned automation";
        draft.FormId = "form-v";

        var v1 = await client.SaveVersionAsync(draft, "v1");

        // Mutate and save a second version so v1 is a restorable prior body.
        draft.Description = "edited body";
        var v2 = await client.SaveVersionAsync(draft, "v2");

        var beforeRestore = await client.ListVersionsAsync(v1.ContentItemId);
        Assert.Equal(2, beforeRestore.Versions.Count);

        var restore = await client.RestoreVersionAsync(v1.ContentItemId, v1.VersionId);

        Assert.Null(restore.BindingState);
        Assert.Equal(v1.VersionId, restore.RestoredFromVersionId);
        Assert.True(restore.NewVersionNumber > v2.VersionNumber);
        Assert.NotEqual(v1.VersionId, restore.NewVersionId);

        var afterRestore = await client.ListVersionsAsync(v1.ContentItemId);
        // Append-only: history grew by one; the original v1 still exists untouched.
        Assert.Equal(3, afterRestore.Versions.Count);
        Assert.Contains(afterRestore.Versions, version => version.VersionId == v1.VersionId);
        Assert.Equal(restore.NewVersionNumber, afterRestore.Versions[0].VersionNumber);
        Assert.True(afterRestore.Versions[0].IsCurrent);
    }

    [Fact]
    public async Task GetVersionReturnsTheImmutableBodyForPreview()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync("new")).Draft!;
        draft.Name = "Preview me";
        draft.FormId = "form-preview";
        var save = await client.SaveVersionAsync(draft, "v1");

        var body = await client.GetVersionAsync(save.ContentItemId, save.VersionId);

        Assert.NotNull(body);
        Assert.Equal("form-preview", body!.FormId);
        Assert.Equal("Preview me", body.Name);
    }

    [Fact]
    public async Task ListVersionsForNeverSavedContentItemIsEmptyNotBlocked()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();

        var history = await client.ListVersionsAsync(contentItemId: string.Empty);

        Assert.Null(history.BindingState);
        Assert.Empty(history.Versions);
    }

    [Fact]
    public async Task SaveRejectsBlankChangeNote()
    {
        var client = InMemoryCollectAutomationClient.CreateSeeded();
        var draft = (await client.OpenEditorAsync(InMemoryCollectAutomationClient.SeedDraftId)).Draft!;

        await Assert.ThrowsAsync<ArgumentException>(() => client.SaveVersionAsync(draft, "   "));
    }

    [Fact]
    public async Task UnsupportedClientSurfacesMissingBindingOnEveryOperation()
    {
        var client = new UnsupportedCollectAutomationClient();

        Assert.Empty(await client.ListAutomationsAsync());

        var editor = await client.OpenEditorAsync("anything");
        Assert.NotNull(editor.BindingState);
        Assert.Null(editor.Draft);

        var save = await client.SaveVersionAsync(new CollectAutomationDraft { DraftId = "d" }, "note");
        Assert.NotNull(save.BindingState);
        Assert.Empty(save.VersionId);

        var history = await client.ListVersionsAsync("content");
        Assert.NotNull(history.BindingState);

        Assert.Null(await client.GetVersionAsync("content", "v1"));

        var restore = await client.RestoreVersionAsync("content", "v1");
        Assert.NotNull(restore.BindingState);
    }

    [Fact]
    public async Task UnsupportedMissingBindingNamesTheServerBaseUrlContract()
    {
        // The blocked surface must point the operator at the real binding contract, never seeded data.
        var client = new UnsupportedCollectAutomationClient();
        var save = await client.SaveVersionAsync(new CollectAutomationDraft { DraftId = "d" }, "note");

        Assert.Equal("Honua:Server:BaseUrl", save.BindingState!.Contract);
        Assert.Contains("Honua.Collect.Core", save.BindingState.Detail, StringComparison.Ordinal);
    }
}
