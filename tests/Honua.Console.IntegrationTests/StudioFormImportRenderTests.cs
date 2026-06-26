using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the XLSForm import page (<c>/studio/form/import</c>). Verifies the
/// missing-binding surface, that an uploaded workbook renders the imported draft's preview + the importer
/// diagnostics, that a successful save unlocks "Open in builder", and that an unsupported server surfaces the
/// honest unavailable state. Drives the page through a fake <see cref="IStudioFormPackageDataSource"/> and a
/// real <see cref="InputFile"/> upload rather than a mock server.
/// </summary>
public sealed class StudioFormImportRenderTests
{
    private static readonly StudioFormCapabilityState MissingBinding = new(
        "Form builder",
        "Missing binding",
        "Honua:Server:BaseUrl",
        "Configure Honua:Server:BaseUrl so the form builder can bind the server-owned form package lifecycle.");

    [Fact]
    public void Import_WhenBindingMissing_RendersNotBoundSurface()
    {
        var data = new FakeImportDataSource { LoadResult = new StudioFormEditorLoad(null, [MissingBinding]) };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.Render<StudioFormImportPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Form authoring is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.DoesNotContain("data-form-import-file", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WhenBound_RendersUploadSurface()
    {
        var data = new FakeImportDataSource { LoadResult = new StudioFormEditorLoad(new StudioFormEditorState(), []) };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.Render<StudioFormImportPage>();

        page.WaitForAssertion(
            () => Assert.Contains("data-form-import-file", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Import an XLSForm", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_AfterUpload_RendersPreviewDiagnostics_AndSaveUnlocksOpen()
    {
        var imported = new StudioFormEditorState { Title = "Water point survey" };
        imported.Fields.Add(new StudioFormFieldEditor { FieldId = "name", Label = "Name", Type = "text" });
        var data = new FakeImportDataSource
        {
            LoadResult = new StudioFormEditorLoad(new StudioFormEditorState(), []),
            ImportOutcome = new StudioFormImportOutcome
            {
                Status = StudioFormImportStatuses.Imported,
                State = imported,
                Message = "Imported \"Water point survey\" — 1 field(s).",
                Diagnostics = [new StudioFormImportDiagnostic("warning", "xlsform.unsupportedType", "survey!B7", "The 'rank' type was imported as text.")]
            }
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.Render<StudioFormImportPage>();
        page.WaitForAssertion(
            () => Assert.Contains("data-form-import-file", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("type,name,label", "survey.xlsx"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("data-form-import-result", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Water point survey", page.Markup, StringComparison.Ordinal);
                Assert.Contains("data-form-import-diagnostics", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        // Open-in-builder is hidden until the imported draft is saved to the server.
        Assert.DoesNotContain("data-form-import-open", page.Markup, StringComparison.Ordinal);

        FindButton(page, "Save as draft").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("data-form-import-open", page.Markup, StringComparison.Ordinal);
                Assert.Contains("data-form-import-saved", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Import_WhenServerUnsupported_RendersUnavailableState()
    {
        var data = new FakeImportDataSource
        {
            LoadResult = new StudioFormEditorLoad(new StudioFormEditorState(), []),
            ImportOutcome = StudioFormImportOutcome.Failed(
                StudioFormImportStatuses.Unsupported, "This server does not offer XLSForm import yet.")
        };
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(data);

        var page = ctx.Render<StudioFormImportPage>();
        page.WaitForAssertion(
            () => Assert.Contains("data-form-import-file", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("x", "survey.xlsx"));

        page.WaitForAssertion(
            () => Assert.Contains("data-form-import-failed", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("does not offer XLSForm import", page.Markup, StringComparison.Ordinal);
    }

    private static IElement FindButton(IRenderedComponent<StudioFormImportPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    private sealed class FakeImportDataSource : IStudioFormPackageDataSource
    {
        public StudioFormEditorLoad LoadResult { get; set; } = new(new StudioFormEditorState(), []);

        public StudioFormImportOutcome ImportOutcome { get; set; } =
            StudioFormImportOutcome.Failed(StudioFormImportStatuses.Error, "no outcome configured");

        public Task<StudioFormImportOutcome> ImportXlsFormAsync(StudioFormImportRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ImportOutcome);

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(LoadResult);

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default)
        {
            var saved = new StudioFormEditorState { FormId = "form-imported-1", Title = state.Title };
            foreach (var field in state.Fields)
            {
                saved.Fields.Add(field);
            }

            return Task.FromResult(new StudioFormCommandResult(true, "Saved.", saved));
        }

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormWorkspace([], []));

        public Task<StudioFormCommandResult> ValidateAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Valid.", state));

        public Task<StudioFormCommandResult> PublishAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Published.", state));

        public Task<StudioFormCommandResult> ReopenAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Reopened.", new StudioFormEditorState { FormId = formId, Version = version + 1 }));

        public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormOfflinePolicyView(false, []));

        public Task<StudioFormAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioFormAiCapability.Off);

        public Task<StudioFormGenerationOutcome> GenerateAsync(StudioFormEditorState currentState, StudioFormGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormGenerationOutcome { Status = StudioFormGenerationStatuses.Unsupported });
    }
}
