using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the Wave-1 validation wiring on the form builder page
/// (<c>/studio/form</c>): inline client errors with <c>aria-invalid</c>, server validation items binding to
/// their fields, and the unsaved-changes dirty guard (editing marks dirty → navigation is guarded; a
/// successful save clears it). Drives the page through a fake <see cref="IStudioFormPackageDataSource"/>.
/// </summary>
public sealed class StudioFormBuilderValidationRenderTests
{
    [Fact]
    public void InvalidRange_ShowsInlineError()
    {
        using var ctx = NewContext();
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(RangeEditor(), []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = OpenEditor(ctx, data);

        // The loaded editor already has an inverted range (min 10 > max 1); the client validator runs on load.
        page.WaitForAssertion(
            () => Assert.Contains("Range minimum must be less than or equal to the maximum", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("console-validation-inline", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateFieldId_ShowsInlineErrorAndAriaInvalidOnFieldRow()
    {
        using var ctx = NewContext();
        var editor = ReadyEditor();
        editor.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Dup" });
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = OpenEditor(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("Field ids must be unique", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // The field-id row (a FieldStateRow) carries aria-invalid + the invalid styling hook.
        Assert.Contains("aria-invalid=\"true\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("console-field-state--invalid", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BlockingClientError_DisablesSaveAndShowsSummary()
    {
        using var ctx = NewContext();
        // A blank-title editor: title is a blocking client rule.
        var editor = ReadyEditor();
        editor.Title = string.Empty;
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = OpenEditor(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("data-validation-summary=\"blocking\"", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.True(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Fact]
    public void ServerValidationItem_SurfacesOnItsFieldInline()
    {
        using var ctx = NewContext();
        var editor = ReadyEditor();
        // The server reports a duplicate-id finding for the authored field.
        editor.LastValidation = new StudioFormValidationView(false,
            [new StudioFormValidationItem("error", "fieldIdDuplicate", "asset_id", "Server says: duplicate field id")]);
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = OpenEditor(ctx, data);

        page.WaitForAssertion(
            () => Assert.Contains("Server says: duplicate field id", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Rendered as an inline field-state error (bound to the field), not only in the flat list.
        Assert.Contains("console-field-state__error", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerBlockingFinding_DoesNotGateSave_AndEditingClearsTheStaleAnnotation()
    {
        using var ctx = NewContext();
        var editor = ReadyEditor();
        // The last server validation reported a blocking finding on a field. The operator must still be able to
        // re-save after fixing it (Validate is disabled while edits are unsaved), so server findings never gate Save.
        editor.LastValidation = new StudioFormValidationView(false,
            [new StudioFormValidationItem("blocker", "fieldIdDuplicate", "asset_id", "Server duplicate id")]);
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(editor, []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = OpenEditor(ctx, data);

        // The server finding surfaces inline, but Save stays enabled (no client blocker).
        page.WaitForAssertion(
            () => Assert.Contains("Server duplicate id", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));

        // Editing invalidates the stale server validation; the annotation is dropped.
        page.Find("input[placeholder='Field inspection form']").Change("Hydrant inspection v2");
        page.WaitForAssertion(
            () => Assert.DoesNotContain("Server duplicate id", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Fact]
    public void Editing_MarksDirty_GuardsNavigation_AndSaveClearsIt()
    {
        using var ctx = NewContext();
        var data = new FakeFormDataSource { EditorLoad = new StudioFormEditorLoad(ReadyEditor(), []) };
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);
        // Operator declines the leave prompt, so a dirty guard prevents navigation.
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);

        var page = OpenEditor(ctx, data);
        var nav = ctx.Services.GetRequiredService<FakeNavigationManager>();

        // Clean: navigation proceeds.
        nav.NavigateTo("catalog");
        Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
        nav.NavigateTo("studio/form");

        // Edit a bound field -> dirty.
        page.Find("input[placeholder='Field inspection form']").Change("Hydrant inspection v2");

        // Dirty: the guard prevents navigation (confirm returns false), so the URI stays on the editor.
        nav.NavigateTo("catalog");
        page.WaitForAssertion(
            () => Assert.EndsWith("studio/form", nav.Uri, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Save clears dirty; navigation proceeds again.
        FindButton(page, "Save draft").Click();
        page.WaitForAssertion(
            () =>
            {
                nav.NavigateTo("catalog");
                Assert.EndsWith("catalog", nav.Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static Bunit.TestContext NewContext()
    {
        var ctx = new Bunit.TestContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        return ctx;
    }

    private static IRenderedComponent<StudioFormBuilderPage> OpenEditor(
        Bunit.TestContext ctx,
        FakeFormDataSource data)
    {
        var page = ctx.RenderComponent<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, FakeFormDataSource.OpenLabel), TimeSpan.FromSeconds(5));
        FindButton(page, FakeFormDataSource.OpenLabel).Click();
        page.WaitForAssertion(
            () => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        return page;
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
            LastValidation = new StudioFormValidationView(true, []),
        };
        state.Fields.Add(new StudioFormFieldEditor { FieldId = "asset_id", Label = "Asset ID", TargetField = "asset_id", Required = true });
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    private static StudioFormEditorState RangeEditor()
    {
        var state = ReadyEditor();
        state.FormId = "form-range";
        state.Title = "Range form";
        state.Fields.Clear();
        state.Fields.Add(new StudioFormFieldEditor
        {
            FieldId = "temperature",
            Label = "Temperature",
            TargetField = "temperature",
            DomainKind = "range",
            RangeMin = "10",
            RangeMax = "1",
        });
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    private sealed class FakeFormDataSource : IStudioFormPackageDataSource
    {
        public Task<StudioFormAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioFormAiCapability.Off);

        public Task<StudioFormGenerationOutcome> GenerateAsync(
            StudioFormEditorState currentState, StudioFormGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormGenerationOutcome { Status = StudioFormGenerationStatuses.Unsupported });

        /// <summary>Stable list-button label so tests can open the editor regardless of the editor's title.</summary>
        public const string OpenLabel = "Open form";

        public StudioFormEditorLoad EditorLoad { get; set; } = new(null, []);

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            var state = EditorLoad.State;
            var packages = state is null
                ? Array.Empty<StudioFormPackageListItem>()
                : new[]
                {
                    new StudioFormPackageListItem(
                        state.FormId ?? "form-1", OpenLabel, state.ServiceId, state.LayerId, state.Version, null, DateTimeOffset.UtcNow),
                };
            return Task.FromResult(new StudioFormWorkspace(packages, []));
        }

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EditorLoad);

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default)
        {
            // A successful save returns the editor on a clean, server-synced baseline.
            state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
            return Task.FromResult(new StudioFormCommandResult(true, "Saved.", state));
        }

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
