using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the form builder page (<c>/studio/form</c>). Verifies the missing-binding
/// surface, that the editor renders the authored fields and the explicit offline-policy review (AC#2), and
/// that Publish stays gated until the submit target is validated and the offline policy reviewed (AC#2/AC#3).
/// Drives the page through a fake <see cref="IStudioFormPackageDataSource"/> rather than a mock server.
/// </summary>
public sealed class StudioFormBuilderRenderTests
{
    [Fact]
    public void FormBuilder_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace([], [MissingBinding])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Form package lifecycle is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-form-builder", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FormBuilder_OpenReadyForm_RendersFieldsAndEnablesPublish()
    {
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-1", "Hydrant inspection", "inspections", 7, 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant inspection").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Offline policy", page.Markup, StringComparison.Ordinal);
        Assert.Contains("I reviewed the offline/sync policy", page.Markup, StringComparison.Ordinal);
        Assert.False(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void FormBuilder_OpenPublishedForm_DisablesPublishAndOffersReopen()
    {
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-1", "Hydrant inspection", "inspections", 7, 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(PublishedEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant inspection").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // A published version is terminal in the builder: Publish stays disabled even though the offline-review
        // flag is still editable (the case that previously re-enabled it), the only forward action is Reopen,
        // and the draft-only pre-publish gate is not shown.
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
        Assert.NotNull(FindButton(page, "Reopen as draft"));
        Assert.DoesNotContain("Resolve before publish", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FormBuilder_OpenIncompleteForm_GatesPublishWithUnmetRequirements()
    {
        var incomplete = new StudioFormEditorState { FormId = "form-2", Title = "Incomplete" };
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-2", "Incomplete", string.Empty, 0, 1, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(incomplete, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Incomplete"), TimeSpan.FromSeconds(5));
        FindButton(page, "Incomplete").Click();

        page.WaitForAssertion(
            () => Assert.Contains("Resolve before publish", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
    }

    [Fact]
    public void FormBuilder_EditAfterValidation_ReGatesPublishAndValidate()
    {
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-1", "Hydrant inspection", "inspections", 7, 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant inspection").Click();
        page.WaitForAssertion(
            () => Assert.False(FindButton(page, "Publish").HasAttribute("disabled")),
            TimeSpan.FromSeconds(5));

        // Editing a bound field after the server validation invalidates that result: publish and the
        // (server-saved) validate must re-gate until the draft is saved and validated again.
        page.Find("input[placeholder='Field inspection form']").Change("Hydrant inspection v2");

        Assert.True(FindButton(page, "Publish").HasAttribute("disabled"));
        Assert.True(FindButton(page, "Validate").HasAttribute("disabled"));
        Assert.Contains("out of date", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormBuilder_BackToForms_ReloadsListAfterAsyncCompletion()
    {
        var data = new FakeFormDataSource
        {
            SimulateAsyncWorkspace = true,
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-1", "Hydrant inspection", "inspections", 7, 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(ReadyEditor(), [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant inspection").Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        FindButton(page, "Back to forms").Click();

        // The reload completes asynchronously; the awaited handler must rerender back to the list.
        page.WaitForAssertion(
            () =>
            {
                Assert.DoesNotContain("data-form-builder", page.Markup, StringComparison.Ordinal);
                Assert.NotNull(FindButton(page, "New form"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void FormBuilder_OpenForm_RendersFieldStatePillsAcrossStates()
    {
        // A field with a blank TargetField surfaces as discovered (the server defaults it from the field id);
        // an attachment field is system-managed. The existing form identity renders as a system row.
        var editor = ReadyEditor();
        editor.Fields.Clear();
        editor.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset ID", TargetField = string.Empty });
        editor.Fields.Add(new StudioFormFieldEditor { FieldId = "photo", Label = "Photo", Type = "attachment" });
        var data = new FakeFormDataSource
        {
            Workspace = new StudioFormWorkspace(
                [new StudioFormPackageListItem("form-1", "Hydrant inspection", "inspections", 7, 2, null, DateTimeOffset.UtcNow)],
                []),
            EditorLoad = new StudioFormEditorLoad(editor, [])
        };
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"), TimeSpan.FromSeconds(5));
        FindButton(page, "Hydrant inspection").Click();

        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Input pills for operator-entered fields (Title, Field id, Label, …).
        Assert.NotEmpty(page.FindAll(".console-field-state--input .console-field-state__pill--input"));
        // The server-assigned form id renders as a system row.
        Assert.Contains("console-field-state--system", page.Markup, StringComparison.Ordinal);
        // The blank target field reads as discovered (server defaults it from the field id).
        Assert.Contains("console-field-state--discovered", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-field-state=\"discovered\"", page.Markup, StringComparison.Ordinal);
    }

    private static IElement FindButton(IRenderedComponent<StudioFormBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private static StudioFormEditorState ReadyEditor()
    {
        var state = new StudioFormEditorState
        {
            FormId = "form-1",
            Version = 2,
            Title = "Hydrant inspection",
            ServiceId = "inspections",
            LayerId = 7,
            ETag = "etag-2",
            OfflineEnabled = true,
            ReplicaTransportEnabled = true,
            OfflinePolicyReviewed = true,
            LastValidation = new StudioFormValidationView(true, [])
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset ID", TargetField = "asset_id", Required = true });
        // Represent a draft freshly loaded from the server (validated, with no unsaved edits).
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    private static StudioFormEditorState PublishedEditor()
    {
        var state = ReadyEditor();
        state.Status = HonuaFormStatuses.Published;
        // A published version carries no server-side offline-review acknowledgment, so a freshly opened
        // published form loads unreviewed — the exact case where checking the box used to re-enable Publish.
        // Recompute the baseline signature after flipping status so the form is not treated as dirty.
        state.OfflinePolicyReviewed = false;
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    private static readonly StudioFormCapabilityState MissingBinding = new(
        "Form builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the form builder can bind the server-owned form package lifecycle.");

    private sealed class FakeFormDataSource : IStudioFormPackageDataSource
    {
        public StudioFormWorkspace Workspace { get; set; } = new([], []);

        public StudioFormEditorLoad EditorLoad { get; set; } = new(null, []);

        /// <summary>When set, the workspace fetch completes asynchronously to mimic the real HTTP path.</summary>
        public bool SimulateAsyncWorkspace { get; set; }

        public async Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            if (SimulateAsyncWorkspace)
            {
                await Task.Yield();
            }

            return Workspace;
        }

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Saved.", state));

        public Task<StudioFormCommandResult> ValidateAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Valid.", state, new StudioFormValidationView(true, [])));

        public Task<StudioFormCommandResult> PublishAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Published.", state));

        public Task<StudioFormCommandResult> ReopenAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Reopened.", new StudioFormEditorState { FormId = formId, Version = version + 1 }));

        public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormOfflinePolicyView(true, ["feature-server-replica"]));
    }
}
