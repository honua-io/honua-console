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

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Workspace);

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
