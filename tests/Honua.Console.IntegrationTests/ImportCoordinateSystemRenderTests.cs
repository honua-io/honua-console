using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ImportCoordinateSystemRenderTests
{
    [Fact]
    public void DetectedSystemDefaultsToKeepSource()
    {
        using var context = new BunitContext();
        var import = Register(context);
        var changed = new List<ConsoleFileImportCoordinates?>();
        var step = context.Render<ImportCoordinateSystemStep>(parameters => parameters
            .Add(component => component.CoordinateSystem, import.System)
            .Add(component => component.AuthorityBinding, "operator-environment")
            .Add(component => component.Changed, value => changed.Add(value)));
        Assert.Contains("EPSG:2193", step.Find("[data-import-detected-system]").TextContent);
        Assert.Contains("New Zealand", step.Markup);
        Assert.True(step.FindAll("input[type=radio]")[0].HasAttribute("checked"));
        Assert.Equal(new ConsoleFileImportCoordinates(null, null), Assert.Single(changed));
        Assert.Empty(step.FindAll("select"));
    }

    [Fact]
    public void ReprojectionNeedsAnExplicitLookupChoice()
    {
        using var context = new BunitContext();
        var import = Register(context);
        ConsoleFileImportCoordinates? selected = null;
        var step = context.Render<ImportCoordinateSystemStep>(parameters => parameters
            .Add(component => component.CoordinateSystem, import.System)
            .Add(component => component.AuthorityBinding, "operator-environment")
            .Add(component => component.Changed, value => selected = value));
        step.FindAll("input[type=radio]")[1].Change(true);
        Assert.Null(selected);
        step.Find("input[placeholder='EPSG code or name']").Input("4326");
        step.Find("button").Click();
        step.Find("select[aria-label='Target coordinate system']").Change("4326");
        Assert.Equal(new ConsoleFileImportCoordinates(null, 4326), selected);
        step.FindAll("input[type=radio]")[0].Change(true);
        Assert.Equal(new ConsoleFileImportCoordinates(null, null), selected);
        Assert.Empty(step.FindAll("select"));
    }

    [Fact]
    public void UndeclaredSourceRequiresAChoiceEvenWhenReprojecting()
    {
        using var context = new BunitContext();
        var import = Register(context);
        import.System = new() { DetectionMethod = "unknown", RequiresSourceOverride = true };
        ConsoleFileImportCoordinates? selected = null;
        var step = context.Render<ImportCoordinateSystemStep>(parameters => parameters
            .Add(component => component.CoordinateSystem, import.System)
            .Add(component => component.AuthorityBinding, "operator-environment")
            .Add(component => component.Changed, value => selected = value));
        Assert.Null(selected);
        var source = step.FindComponent<CoordinateSystemPicker>();
        source.Find("input").Input("2193");
        source.Find("button").Click();
        source.Find("select").Change("2193");
        Assert.Equal(new ConsoleFileImportCoordinates(2193, null), selected);
        step.FindAll("input[type=radio]")[1].Change(true);
        Assert.Null(selected);
    }

    [Fact]
    public async Task ChangingQueryDiscardsAnInFlightSearch()
    {
        using var context = new BunitContext();
        var import = Register(context);
        var completion = new TaskCompletionSource<OperateSectionResult<HonuaAdminCoordinateSystemSearch>>();
        import.Search = completion.Task;
        var picker = context.Render<CoordinateSystemPicker>(parameters => parameters
            .Add(component => component.Label, "Target coordinate system")
            .Add(component => component.AuthorityBinding, "operator-environment"));
        picker.Find("input").Input("2193");
        var pending = picker.Find("button").ClickAsync(new());
        picker.Find("input").Input("4326");
        await context.Renderer.Dispatcher.InvokeAsync(() => completion.SetResult(import.Results));
        await pending;
        Assert.Empty(picker.FindAll("select"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileImportUsesPreviewAndNeverInventsATarget(bool enabled)
    {
        using var context = new BunitContext();
        var import = Register(context, enabled);
        var page = context.Render<OperateImportFilePage>();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("projected", "projected.gpkg"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-import-coordinate-system]")));
        var registry = context.Services.GetRequiredService<ConsoleAgentContextRegistry>();
        Assert.Equal(enabled ? "/operate/resources/import" : "/", registry.Current.Route);
        Assert.Equal(enabled ? 1 : 0, page.FindAll("section.console-panel[aria-labelledby]").Count);
        if (enabled)
        {
            Assert.Equal(2, registry.Current.Unsaved.ChangedFieldCount);
            Assert.Empty(registry.Current.Entities);
            Assert.DoesNotContain("projected.gpkg", System.Text.Json.JsonSerializer.Serialize(registry.Current));
            CatalogAdminPreview.Capture("data-import-file", page.Markup);
        }
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.NotNull(import.Command));
        Assert.Null(import.Command!.TargetSrid);
        Assert.Null(import.Command.SourceSrid);
        Assert.Equal("operator-environment", import.Command.AuthorityBinding);
        Assert.Equal("projected", System.Text.Encoding.UTF8.GetString(import.Command.FileContent));
        Assert.Equal("EPSG:2193", page.Find("[data-import-stored-system]").TextContent);
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
        Assert.Equal(0, registry.Current.Unsaved.PendingOperationCount);
        if (enabled) CatalogAdminPreview.Capture("data-import-file-complete", page.Markup);
    }

    [Fact]
    public void NewFileDropsThePreviousChoices()
    {
        using var context = new BunitContext();
        var import = Register(context);
        var page = context.Render<OperateImportFilePage>();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("projected", "projected.gpkg"));
        page.FindAll("input[type=radio]")[1].Change(true);
        var picker = page.FindComponent<CoordinateSystemPicker>();
        picker.Find("input").Input("4326");
        picker.Find("button").Click();
        picker.Find("select").Change("4326");
        import.System = new() { DetectionMethod = "unknown", RequiresSourceOverride = true };
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("unknown", "unknown.gpkg"));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("input[aria-label='Find source coordinate system']")));
        Assert.True(page.FindAll("input[type=radio]")[0].HasAttribute("checked"));
        Assert.True(page.Find("button.console-button:not(.console-button-secondary)").HasAttribute("disabled"));
        Assert.Null(import.Command);
    }

    [Fact]
    public void LostResponseLocksImportWithoutDiscardingTheReason()
    {
        using var context = new BunitContext();
        var import = Register(context);
        import.Outcome = new() { State = ConsoleWriteOutcome.UnverifiedState, Detail = ConsoleWriteOutcome.UnverifiedDetail };
        var page = context.Render<OperateImportFilePage>();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("projected", "projected.gpkg"));
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Contains(ConsoleWriteOutcome.UnverifiedDetail, page.Markup));
        page.Find("input[placeholder=parcels]").Input("another_name");
        Assert.Contains(ConsoleWriteOutcome.UnverifiedDetail, page.Markup);
        Assert.True(page.Find("button.console-button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task OversizeFileExplainsTheLimitWithoutAnImpossibleRetry()
    {
        using var context = new BunitContext();
        Register(context);
        var page = context.Render<OperateImportFilePage>();
        await page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new UnreadableFile(101L * 1024 * 1024)])));

        Assert.Contains("100 MB", page.Markup);
        Assert.DoesNotContain("Choose it again and retry", page.Markup);
    }

    [Fact]
    public async Task UnreadableFileKeepsItsReadFailureReason()
    {
        using var context = new BunitContext();
        Register(context);
        var page = context.Render<OperateImportFilePage>();
        await page.InvokeAsync(() => page.FindComponent<InputFile>().Instance.OnChange.InvokeAsync(
            new InputFileChangeEventArgs([new UnreadableFile(5)])));

        Assert.Contains("could not be read", page.Markup);
    }

    [Fact]
    public void MismatchedCoordinatesLockTheMutationAndKeepItsReason()
    {
        using var context = new BunitContext();
        var import = Register(context);
        import.Outcome = new()
        {
            State = ConsoleFileImportResult.CoordinateSystemMismatchState,
            Detail = "Read the original environment to check the imported table.",
            StoredSrid = 3857,
            Reprojected = true
        };
        var page = context.Render<OperateImportFilePage>();
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("projected", "projected.gpkg"));
        page.Find("button.console-button").Click();
        page.WaitForAssertion(() => Assert.Contains("Check the imported coordinate system", page.Markup));
        page.Find("input[placeholder=parcels]").Input("another_name");

        Assert.Contains("Read the original environment", page.Markup);
        Assert.Equal("EPSG:3857", page.Find("[data-import-stored-system]").TextContent);
        Assert.True(page.Find("button.console-button").HasAttribute("disabled"));
        Assert.True(page.Find("input[type=file]").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("a[href='/operate/publishing/quick']"));
    }

    private sealed class UnreadableFile(long size) : IBrowserFile
    {
        public string Name => "projected.gpkg";
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => size;
        public string ContentType => "application/octet-stream";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            throw new IOException("The device could not read the selected file.");
    }

    private static ImportOperation Register(BunitContext context, bool enabled = false)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        var import = new ImportOperation();
        context.Services.AddSingleton<IConsoleFileImportOperation>(import);
        return import;
    }

    private sealed class ImportOperation : IConsoleFileImportOperation
    {
        public HonuaAdminImportCoordinateSystem System { get; set; } = new()
        {
            DetectedSrid = 2193,
            EffectiveSourceSrid = 2193,
            StoredSrid = 2193,
            DetectionMethod = "file-metadata",
            SourceCrsDeclared = true,
            DetectedCrs = new() { Code = 2193, Name = "New Zealand Transverse Mercator 2000" }
        };
        public ConsoleFileImportCommand? Command { get; private set; }
        public ConsoleFileImportResult Outcome { get; set; } = new()
        { Succeeded = true, State = "Imported", StoredSrid = 2193, Reprojected = false, TableName = "projected" };
        public Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>>? Search { get; set; }
        public OperateSectionResult<HonuaAdminCoordinateSystemSearch> Results => OperateSectionResult<HonuaAdminCoordinateSystemSearch>.Allowed(new()
        { Items = [new() { Code = 2193, Name = "New Zealand Transverse Mercator 2000" }, new() { Code = 4326, Name = "WGS 84" }] });
        public Task<IReadOnlyList<string>> GetSupportedExtensionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([".gpkg"]);
        public Task<ConsoleFileImportPreview> PreviewAsync(byte[] bytes, string name, CancellationToken cancellationToken = default)
            => Task.FromResult(new ConsoleFileImportPreview
            { Succeeded = true, CoordinateSystem = System, AuthorityBinding = "operator-environment" });
        public Task<OperateSectionResult<HonuaAdminCoordinateSystemSearch>> SearchCoordinateSystemsAsync(string query, string binding, CancellationToken cancellationToken = default) =>
            Search ?? Task.FromResult(Results);
        public Task<ConsoleFileImportResult> ImportAsync(ConsoleFileImportCommand command, CancellationToken cancellationToken = default)
        { Command = command; return Task.FromResult(Outcome); }
    }
}
