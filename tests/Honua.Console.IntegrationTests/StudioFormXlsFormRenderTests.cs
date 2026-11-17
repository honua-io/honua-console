using AngleSharp.Dom;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// XLSForm import from the forms list and export from a published form.
public sealed class StudioFormXlsFormRenderTests
{
    private static readonly StudioFormPackageListItem Published =
        new("form-1", "Hydrant inspection", "inspections", 7, null, 2, DateTimeOffset.UtcNow);

    [Fact]
    public void FormsList_WithoutXlsFormBinding_OffersNoImport()
    {
        using var ctx = CreateContext(new FormData(), xlsForm: null);

        var page = RenderList(ctx);

        Assert.DoesNotContain("Import XLSForm", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_RejectedWorkbook_ListsEveryBlockingCellAndOpensNoDraft()
    {
        var forms = new FormData();
        var xlsForm = new XlsFormData
        {
            Import = StudioFormXlsFormOutcome.Rejected(
                "The workbook is not an XLSForm survey a Honua form package can represent.",
                [
                    Cell(("survey", 4, "type"), "Question type 'range' is not supported."),
                    Cell(("choices", 9, "name"), "Choice name is required.")
                ])
        };
        using var ctx = CreateContext(forms, xlsForm);
        var page = RenderList(ctx);

        SubmitImport(page, "inspections", "7");

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-xlsform-diagnostics] tbody tr").Count));
        Assert.Contains("Question type 'range' is not supported.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No draft was created", page.Markup, StringComparison.Ordinal);
        Assert.Empty(forms.Loaded);
        var request = Assert.Single(xlsForm.Imports);
        Assert.Equal(("inspections", 7, "hydrants.xlsx"), (request.ServiceId, request.LayerId, request.FileName));
        Assert.Equal("PK-workbook"u8.ToArray(), request.Content);
    }

    [Fact]
    public void Import_AcceptedWorkbook_OpensTheNewDraft()
    {
        var forms = new FormData { Editor = new StudioFormEditorState { FormId = "hydrants", Version = 1, Title = "Hydrants" } };
        var xlsForm = new XlsFormData
        {
            Import = StudioFormXlsFormOutcome.Imported("Imported hydrants.xlsx as draft version 1 of hydrants.", "hydrants")
        };
        using var ctx = CreateContext(forms, xlsForm);
        var page = RenderList(ctx);

        SubmitImport(page, "inspections", "7");

        page.WaitForAssertion(() => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal));
        Assert.Equal(["hydrants"], forms.Loaded);
        Assert.Empty(page.FindAll("[data-xlsform-import]"));
        Assert.Contains("Imported hydrants.xlsx as draft version 1 of hydrants.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_PublishedForm_DownloadsTheServerWorkbook()
    {
        var workbook = new HonuaFormXlsFormWorkbook("form-1-v2.xlsx", HonuaFormXlsFormHttpClient.WorkbookContentType, [1, 2, 3]);
        var xlsForm = new XlsFormData { Export = StudioFormXlsFormOutcome.Exported("Exported form-1-v2.xlsx.", workbook) };
        using var ctx = CreateContext(new FormData { Editor = PublishedEditor() }, xlsForm);
        var download = ctx.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-file-download.js");
        download.SetupVoid("save", _ => true).SetVoidResult();
        var page = OpenPublished(ctx);

        FindButton(page, "Export XLSForm").Click();

        page.WaitForAssertion(() => Assert.Contains("Exported form-1-v2.xlsx.", page.Markup, StringComparison.Ordinal));
        var save = Assert.Single(download.Invocations, invocation => invocation.Identifier == "save");
        Assert.Equal("form-1-v2.xlsx", save.Arguments[0]);
        Assert.Equal(new byte[] { 1, 2, 3 }, save.Arguments[2]);
        Assert.Equal(["form-1"], xlsForm.Exports);
    }

    [Fact]
    public void Export_UnrepresentableForm_ListsDiagnosticsAndDownloadsNothing()
    {
        var xlsForm = new XlsFormData
        {
            Export = StudioFormXlsFormOutcome.Rejected(
                "The published package uses behavior XLSForm cannot represent.",
                [Cell(("survey", 0, "relevant"), "Visibility rule on 'notes' cannot be written as XLSForm.")])
        };
        using var ctx = CreateContext(new FormData { Editor = PublishedEditor() }, xlsForm);
        var page = OpenPublished(ctx);

        FindButton(page, "Export XLSForm").Click();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-xlsform-diagnostics] tbody tr")));
        Assert.Contains("cannot be written as XLSForm", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(ctx.JSInterop.Invocations, invocation =>
            invocation.Identifier == "import" && Equals(invocation.Arguments[0], "./_content/Honua.Console.Shell/console-file-download.js"));
    }

    [Fact]
    public void DraftForm_OffersNoExport()
    {
        var draft = PublishedEditor();
        draft.Status = HonuaFormStatuses.Draft;
        using var ctx = CreateContext(new FormData { Editor = draft }, new XlsFormData());
        var page = OpenPublished(ctx);

        Assert.DoesNotContain("Export XLSForm", page.Markup, StringComparison.Ordinal);
    }

    private static BunitContext CreateContext(FormData forms, XlsFormData? xlsForm)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        forms.Workspace = new StudioFormWorkspace([Published], []);
        ctx.Services.AddSingleton<IStudioFormPackageDataSource>(forms);
        if (xlsForm is not null)
        {
            ctx.Services.AddSingleton<IStudioFormXlsFormDataSource>(xlsForm);
        }

        return ctx;
    }

    private static IRenderedComponent<StudioFormBuilderPage> RenderList(BunitContext ctx)
    {
        var page = ctx.Render<StudioFormBuilderPage>();
        page.WaitForAssertion(() => FindButton(page, "Hydrant inspection"));
        return page;
    }

    private static IRenderedComponent<StudioFormBuilderPage> OpenPublished(BunitContext ctx)
    {
        var page = RenderList(ctx);
        FindButton(page, "Hydrant inspection").Click();
        page.WaitForAssertion(() => Assert.Contains("data-form-builder", page.Markup, StringComparison.Ordinal));
        return page;
    }

    private static void SubmitImport(IRenderedComponent<StudioFormBuilderPage> page, string serviceId, string layerId)
    {
        FindButton(page, "Import XLSForm").Click();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary("PK-workbook"u8.ToArray(), "hydrants.xlsx"));
        var inputs = page.FindAll("[data-xlsform-import] input.console-input:not([type=file])");
        inputs[0].Input(serviceId);
        page.FindAll("[data-xlsform-import] input[type=number]")[0].Input(layerId);
        FindButton(page, "Import").Click();
    }

    private static IElement FindButton(IRenderedComponent<StudioFormBuilderPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Trim() == label);

    private static HonuaFormXlsFormDiagnostic Cell((string Sheet, int Row, string Column) at, string message) =>
        new() { Sheet = at.Sheet, Row = at.Row, Column = at.Column, Message = message };

    private static StudioFormEditorState PublishedEditor()
    {
        var state = new StudioFormEditorState
        {
            FormId = "form-1",
            Version = 2,
            Title = "Hydrant inspection",
            ServiceId = "inspections",
            LayerId = 7,
            Status = HonuaFormStatuses.Published
        };
        state.SavedSignature = StudioFormPackageMapper.ComputeContentSignature(state);
        return state;
    }

    private sealed class XlsFormData : IStudioFormXlsFormDataSource
    {
        public StudioFormXlsFormOutcome Import { get; init; } = StudioFormXlsFormOutcome.Rejected("Not configured.", []);

        public StudioFormXlsFormOutcome Export { get; init; } = StudioFormXlsFormOutcome.Rejected("Not configured.", []);

        public List<HonuaFormXlsFormUpload> Imports { get; } = [];

        public List<string> Exports { get; } = [];

        public Task<StudioFormXlsFormOutcome> ImportAsync(HonuaFormXlsFormUpload upload, CancellationToken cancellationToken = default)
        {
            Imports.Add(upload);
            return Task.FromResult(Import);
        }

        public Task<StudioFormXlsFormOutcome> ExportAsync(string formId, CancellationToken cancellationToken = default)
        {
            Exports.Add(formId);
            return Task.FromResult(Export);
        }
    }

    private sealed class FormData : IStudioFormPackageDataSource
    {
        public StudioFormWorkspace Workspace { get; set; } = new([], []);

        public StudioFormEditorState? Editor { get; init; }

        public List<string> Loaded { get; } = [];

        public Task<StudioFormWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) => Task.FromResult(Workspace);

        public Task<StudioFormEditorLoad> LoadAsync(string? formId, CancellationToken cancellationToken = default)
        {
            Loaded.Add(formId ?? string.Empty);
            return Task.FromResult(new StudioFormEditorLoad(Editor, []));
        }

        public Task<StudioFormCommandResult> SaveDraftAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Saved.", state));

        public Task<StudioFormCommandResult> ValidateAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Valid.", state));

        public Task<StudioFormCommandResult> PublishAsync(StudioFormEditorState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Published.", state));

        public Task<StudioFormCommandResult> ReopenAsync(string formId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormCommandResult(true, "Reopened.", null));

        public Task<StudioFormOfflinePolicyView> GetOfflinePolicyAsync(string formId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioFormOfflinePolicyView(false, []));
    }
}
