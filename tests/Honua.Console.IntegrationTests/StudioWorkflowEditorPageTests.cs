using AngleSharp.Dom;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// A failed validation keeps the editor dirty so a later publish cannot reuse a stale version and drop the current edits.
public sealed class StudioWorkflowEditorPageTests
{
    [Fact]
    public void RetryEditor_ExplicitGlobalChange_UpdatesNodesWithoutChangingFailurePolicy()
    {
        var client = new RecordingWorkflowPackageClient();
        client.Draft.Nodes.Add(new StudioWorkflowNode { Id = "source", Type = "test.echo", FailurePolicy = WorkflowStepFailurePolicy.Skip });
        client.Draft.Nodes.Add(new StudioWorkflowNode { Id = "sink", Type = "test.echo" });
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);
        var page = ctx.Render<StudioWorkflowEditorPage>(parameters => parameters.Add(p => p.DraftId, RecordingWorkflowPackageClient.PackageId));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("input[type='number'][max='10']")), TimeSpan.FromSeconds(5));
        page.Find("input[type='number'][max='10']").Change("4");
        page.WaitForAssertion(() => Assert.All(client.Draft.Nodes, node => Assert.Equal(4, node.RetryPolicy!.MaxAttempts)));
        Assert.Equal(WorkflowStepFailurePolicy.Skip, client.Draft.Nodes[0].FailurePolicy);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, 0)]
    [InlineData(ConsolePresentationMode.V4, 1)]
    public async Task Publish_AfterValidationFailedSave_DoesNotShipStalePriorVersion(ConsolePresentationMode presentation, int expectedV4Headings)
    {
        var client = new RecordingWorkflowPackageClient();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);
        var registry = OpenWorkflowEditorContext(ctx, presentation);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, RecordingWorkflowPackageClient.PackageId));

        // The draft (with a prior valid version) has loaded once the package-metadata form renders.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Workflow metadata'] input.console-input")),
            TimeSpan.FromSeconds(5));

        Assert.Equal(expectedV4Headings, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal("#workflow-history",
            page.Find("nav[aria-label='Workflow editor sections'] a:nth-child(4)").GetAttribute("href"));
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? "SECTION" : "MAIN", page.Find(".workflow-main").TagName);
        if (presentation == ConsolePresentationMode.V4)
        {
            Assert.Equal("Workflow editor", Assert.Single(page.FindAll("h1")).TextContent);
            CatalogAdminPreview.Capture("workflow-editor-seeded", $"<div class=\"console-shell\" data-console-v4=\"true\">{page.Markup}</div>");
        }

        // Edit the title so the draft diverges from the last saved version (now dirty).
        TitleInput(page).Input("Parcel normalizer (edited)");
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);

        // Save: the server rejects the graph as invalid, so no version is created and the draft stays dirty.
        FindButton(page, "Save draft").Click();
        page.WaitForAssertion(
            () => Assert.Contains(
                "Save blocked: server validation requires attention",
                page.Markup,
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal(1, client.SaveCalls);
        Assert.Equal(0, client.PublishCalls);

        // Publish must NOT proceed on the stale prior version: it re-attempts the save (which fails again) and
        // stops before ever calling PublishAsync.
        FindButton(page, "Publish").Click();
        page.WaitForAssertion(
            () => Assert.Equal(2, client.SaveCalls),
            TimeSpan.FromSeconds(5));
        Assert.Equal(0, client.PublishCalls);
        Assert.Contains(
            "Save blocked: server validation requires attention",
            page.Markup,
            StringComparison.Ordinal);
        Assert.Equal(presentation == ConsolePresentationMode.V4 ? 1 : 0, registry.Current.Unsaved.ChangedFieldCount);
        await ctx.DisposeComponentsAsync();
        Assert.Equal(0, registry.Current.Unsaved.ChangedFieldCount);
    }

    [Fact]
    public void Editor_RendersRunHistoryPanel_WithStateRejectedRowsAndProvenance()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryStudioWorkflowPackageClient.SeedDraftId));

        // Before any run, the panel renders its empty prompt rather than fabricated history.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Workflow run history']")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("No runs yet", page.Markup, StringComparison.Ordinal);

        // Dry-run, then publish: the run-history panel records both runs with their state and evidence.
        FindButton(page, "Run once").Click();
        FindButton(page, "Publish").Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(
                page.FindAll("section[aria-label='Workflow run history'] li.workflow-run-item")),
            TimeSpan.FromSeconds(5));

        var historyMarkup = page.Find("section[aria-label='Workflow run history']").InnerHtml;
        Assert.Contains("succeeded", historyMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rejected rows", historyMarkup, StringComparison.Ordinal);
        Assert.Contains("Workflow history", historyMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_RendersDesignLandmarks_HeaderTabsPaletteAndDagCanvas()
    {
        var client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // The page renders <UnsavedChangesGuard/>, which imports a JS module on dirty transitions; allow the
        // bUnit JSInterop to no-op those calls (the guard is exercised directly in UnsavedChangesGuardTests).
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.Render<StudioWorkflowEditorPage>(
            parameters => parameters.Add(p => p.DraftId, InMemoryStudioWorkflowPackageClient.SeedDraftId));

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("header.workflow-editor-header")),
            TimeSpan.FromSeconds(5));

        // Header bar: editable workflow name, the draft + DAG-valid status badges, and the action buttons
        // moved into the header to match the mockup's top bar.
        var header = page.Find("header.workflow-editor-header");
        Assert.NotEmpty(header.QuerySelectorAll("input.workflow-title-input"));
        Assert.Contains("draft", header.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAG", header.TextContent, StringComparison.Ordinal);
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Run once", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Save draft", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Publish", StringComparison.Ordinal));

        // Section tabs mirror the mockup IA and anchor the canvas/schedule/secrets/history/raw regions.
        var tabRow = page.Find("nav.workflow-tab-row");
        var tabLabels = tabRow.QuerySelectorAll("a").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Equal(["Graph", "Schedule", "Secrets", "Run history", "Contract · raw"], tabLabels);
        foreach (var anchor in new[] { "workflow-graph", "workflow-schedule", "workflow-secrets", "workflow-history", "workflow-package" })
        {
            Assert.NotEmpty(page.FindAll($"#{anchor}"));
        }

        // Step palette renders icon glyphs + category subtitle per node definition.
        var paletteItems = page.FindAll(".workflow-palette .workflow-palette-item");
        Assert.NotEmpty(paletteItems);
        Assert.All(paletteItems, item => Assert.NotEmpty(item.QuerySelectorAll(".workflow-step-glyph")));

        // The DAG canvas draws an SVG edge layer (arrow markers + dotgrid) with absolutely-positioned nodes.
        var canvas = page.Find("#workflow-graph .workflow-canvas");
        Assert.NotEmpty(canvas.QuerySelectorAll("svg.workflow-canvas-edges"));
        Assert.NotEmpty(canvas.QuerySelectorAll("marker#workflow-arrow"));
        Assert.NotEmpty(canvas.QuerySelectorAll(".workflow-canvas-nodes .workflow-node"));
        Assert.NotEmpty(canvas.QuerySelectorAll(".workflow-canvas-edge"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierLoad_CannotReplaceCurrentDraft(ConsolePresentationMode presentation)
    {
        var pending = new TaskCompletionSource<StudioWorkflowEditorContext>();
        var client = new RecordingWorkflowPackageClient { PendingLoad = pending.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        page.Render(p => p.Add(x => x.DraftId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", TitleInput(page).GetAttribute("value")));
        var renders = page.RenderCount;

        pending.SetResult(new StudioWorkflowEditorContext(null, [], new() { Title = "Earlier workflow" }));

        page.WaitForAssertion(() => Assert.True(page.RenderCount > renders));
        Assert.Equal("current", TitleInput(page).GetAttribute("value"));
        await ctx.DisposeComponentsAsync();
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Save draft")]
    [InlineData(ConsolePresentationMode.V4, "Save draft")]
    [InlineData(ConsolePresentationMode.Legacy, "Run once")]
    [InlineData(ConsolePresentationMode.V4, "Run once")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish")]
    [InlineData(ConsolePresentationMode.V4, "Publish")]
    public async Task EarlierSave_CannotClearCurrentEditsOrContinueCommand(ConsolePresentationMode presentation, string command)
    {
        var pending = new TaskCompletionSource<StudioWorkflowSaveResult>();
        var client = new RecordingWorkflowPackageClient { PendingSave = pending.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        page.WaitForAssertion(() => Assert.NotNull(TitleInput(page)));
        TitleInput(page).Input("Edited earlier workflow");
        var saving = page.InvokeAsync(() => FindButton(page, command).ClickAsync(new()));
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        page.Render(p => p.Add(x => x.DraftId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", TitleInput(page).GetAttribute("value")));
        TitleInput(page).Input("Edited current workflow");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);

        pending.SetResult(new() { ContentItemId = "earlier", VersionId = "earlier-version", VersionNumber = 2 });
        await saving;

        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Equal("Edited current workflow", TitleInput(page).GetAttribute("value"));
        Assert.DoesNotContain("earlier-version", page.Markup, StringComparison.Ordinal);
        Assert.Equal(0, client.DryRunCalls);
        Assert.Equal(0, client.PublishCalls);
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task EarlierHistory_CannotReplaceCurrentHistory(ConsolePresentationMode presentation)
    {
        var pending = new TaskCompletionSource<StudioWorkflowRunHistory>();
        var client = new RecordingWorkflowPackageClient { PendingHistory = pending.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        page.WaitForAssertion(() => Assert.NotNull(TitleInput(page)));
        page.Render(p => p.Add(x => x.DraftId, "current"));
        page.WaitForAssertion(() => Assert.Contains("No runs yet", page.Markup, StringComparison.Ordinal));
        var renders = page.RenderCount;

        pending.SetResult(new([new() { RunId = "earlier-run", Status = "earlier-status" }]));

        page.WaitForAssertion(() => Assert.True(page.RenderCount > renders));
        Assert.DoesNotContain("earlier-status", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No runs yet", page.Markup, StringComparison.Ordinal);
        await ctx.DisposeComponentsAsync();
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Run once")]
    [InlineData(ConsolePresentationMode.V4, "Run once")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish")]
    [InlineData(ConsolePresentationMode.V4, "Publish")]
    public async Task EarlierCommand_CannotShowResultOrClearCurrentEdits(ConsolePresentationMode presentation, string command)
    {
        var run = new TaskCompletionSource<StudioWorkflowDryRunResult>();
        var publish = new TaskCompletionSource<StudioWorkflowPublishResult>();
        var client = new RecordingWorkflowPackageClient { PendingRun = run.Task, PendingPublish = publish.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        page.WaitForAssertion(() => Assert.NotNull(TitleInput(page)));
        var running = page.InvokeAsync(() => FindButton(page, command).ClickAsync(new()));
        page.WaitForAssertion(() => Assert.Equal(1, client.DryRunCalls + client.PublishCalls));
        page.Render(p => p.Add(x => x.DraftId, "current"));
        page.WaitForAssertion(() => Assert.Equal("current", TitleInput(page).GetAttribute("value")));
        TitleInput(page).Input("Edited current workflow");

        run.SetResult(new() { Status = "earlier-status" });
        publish.SetResult(new() { PublicationId = "earlier-publication", Status = "earlier-status" });
        await running;

        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.DoesNotContain("earlier-status", page.Markup, StringComparison.Ordinal);
        Assert.Equal("Edited current workflow", TitleInput(page).GetAttribute("value"));
        Assert.False(FindButton(page, "Save draft").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Save draft", "saved-version")]
    [InlineData(ConsolePresentationMode.V4, "Save draft", "saved-version")]
    [InlineData(ConsolePresentationMode.Legacy, "Save draft", null)]
    [InlineData(ConsolePresentationMode.V4, "Save draft", null)]
    [InlineData(ConsolePresentationMode.Legacy, "Run once", "saved-version")]
    [InlineData(ConsolePresentationMode.V4, "Run once", "saved-version")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish", "saved-version")]
    [InlineData(ConsolePresentationMode.V4, "Publish", "saved-version")]
    public async Task PendingCommand_DisablesEditsUntilCompletion(ConsolePresentationMode presentation, string command, string? versionId)
    {
        var save = new TaskCompletionSource<StudioWorkflowSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new TaskCompletionSource<StudioWorkflowDryRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new TaskCompletionSource<StudioWorkflowPublishResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingWorkflowPackageClient
        {
            PendingSave = save.Task,
            PendingRun = run.Task,
            PendingPublish = publish.Task,
            NodeDefinitions = [new() { Type = "source", Label = "Source", Category = StudioWorkflowContractValues.NodeCategorySource }]
        };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "current"));
        TitleInput(page).Input("Edited before command");
        var running = FindButton(page, command).ClickAsync(new());
        try
        {
            page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
            AssertWorkflowInputsDisabled(page);
            Assert.True(page.Find(".workflow-palette-item").HasAttribute("disabled"));
            Assert.True(FindButton(page, "Add Parameter").HasAttribute("disabled"));
            save.SetResult(new() { ContentItemId = "current", VersionId = versionId });
            if (command != "Save draft")
            {
                page.WaitForAssertion(() => Assert.Equal(1, command == "Run once" ? client.DryRunCalls : client.PublishCalls));
                AssertWorkflowInputsDisabled(page);
            }
        }
        finally
        {
            save.TrySetResult(new());
            run.SetResult(new() { Status = "succeeded" });
            publish.SetResult(new() { PublicationId = "publication-current", Status = "queued" });
            await running;
        }

        page.WaitForAssertion(() => Assert.False(TitleInput(page).HasAttribute("disabled")));
        await TitleInput(page).InputAsync(new() { Value = "Edited after completion" });
        page.WaitForAssertion(() => Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Save draft")]
    [InlineData(ConsolePresentationMode.V4, "Save draft")]
    [InlineData(ConsolePresentationMode.Legacy, "Run once")]
    [InlineData(ConsolePresentationMode.V4, "Run once")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish")]
    [InlineData(ConsolePresentationMode.V4, "Publish")]
    public async Task NewDraftCommand_RemainsLockedWhenEarlierCommandCompletes(ConsolePresentationMode presentation, string command)
    {
        var earlier = new TaskCompletionSource<StudioWorkflowSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentSave = new TaskCompletionSource<StudioWorkflowSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new TaskCompletionSource<StudioWorkflowDryRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publish = new TaskCompletionSource<StudioWorkflowPublishResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingWorkflowPackageClient
        {
            SaveHandler = draft => draft.DraftId == "earlier" ? earlier.Task : currentSave.Task,
            PendingRun = run.Task,
            PendingPublish = publish.Task
        };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        TitleInput(page).Input("Edited earlier workflow");
        var earlierCommand = FindButton(page, command).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
        Task currentCommand = Task.CompletedTask;
        try
        {
            page.Render(p => p.Add(x => x.DraftId, "current"));
            page.WaitForAssertion(() => Assert.Equal("current", TitleInput(page).GetAttribute("value")));
            Assert.False(TitleInput(page).HasAttribute("disabled"));
            Assert.False(FindButton(page, command).HasAttribute("disabled"));
            TitleInput(page).Input("Edited current workflow");
            currentCommand = FindButton(page, command).ClickAsync(new());
            page.WaitForAssertion(() => Assert.Equal(2, client.SaveCalls));
            if (command != "Save draft")
            {
                currentSave.SetResult(new() { ContentItemId = "current", VersionId = "current-version" });
                page.WaitForAssertion(() => Assert.Equal(1, client.DryRunCalls + client.PublishCalls));
            }
            AssertWorkflowInputsDisabled(page);

            earlier.SetResult(new() { ContentItemId = "earlier", VersionId = "earlier-version" });
            await earlierCommand;

            AssertWorkflowInputsDisabled(page);
            Assert.True(FindButton(page, command).HasAttribute("disabled"));
            Assert.Equal("Edited current workflow", TitleInput(page).GetAttribute("value"));
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            Assert.DoesNotContain("earlier-version", page.Markup, StringComparison.Ordinal);
            await FindButton(page, "Save draft").ClickAsync(new());
            Assert.Equal(2, client.SaveCalls);
        }
        finally
        {
            earlier.TrySetResult(new());
            currentSave.TrySetResult(new() { ContentItemId = "current", VersionId = "current-version" });
            run.TrySetResult(new() { Status = "succeeded" });
            publish.TrySetResult(new() { PublicationId = "current-publication", Status = "queued" });
            await earlierCommand;
            await currentCommand;
        }

        Assert.False(TitleInput(page).HasAttribute("disabled"));
        Assert.Equal("Edited current workflow", TitleInput(page).GetAttribute("value"));
        TitleInput(page).Input("Edited after current completion");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "Run once")]
    [InlineData(ConsolePresentationMode.V4, "Run once")]
    [InlineData(ConsolePresentationMode.Legacy, "Publish")]
    [InlineData(ConsolePresentationMode.V4, "Publish")]
    public async Task EarlierRunHistoryCompletion_CannotUnlockCurrentSave(ConsolePresentationMode presentation, string command)
    {
        var history = new TaskCompletionSource<StudioWorkflowRunHistory>(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = new TaskCompletionSource<StudioWorkflowSaveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RecordingWorkflowPackageClient { PendingSave = save.Task };
        using var ctx = EditorContext(client, presentation);
        var page = ctx.Render<StudioWorkflowEditorPage>(p => p.Add(x => x.DraftId, "earlier"));
        client.HistoryHandler = id => id == "earlier" ? history.Task : Task.FromResult(StudioWorkflowRunHistory.Empty);
        var earlierCommand = FindButton(page, command).ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, client.DryRunCalls + client.PublishCalls));
        Task currentCommand = Task.CompletedTask;
        try
        {
            page.Render(p => p.Add(x => x.DraftId, "current"));
            page.WaitForAssertion(() => Assert.Equal("current", TitleInput(page).GetAttribute("value")));
            Assert.False(TitleInput(page).HasAttribute("disabled"));
            TitleInput(page).Input("Current unsaved workflow");
            currentCommand = FindButton(page, "Save draft").ClickAsync(new());
            page.WaitForAssertion(() => Assert.Equal(1, client.SaveCalls));
            AssertWorkflowInputsDisabled(page);

            history.SetResult(new([new() { RunId = "earlier-run", Status = "earlier-completed" }]));
            await earlierCommand;

            AssertWorkflowInputsDisabled(page);
            Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
            Assert.DoesNotContain("earlier-completed", page.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("pub-stale", page.Markup, StringComparison.Ordinal);
            Assert.Contains("No runs yet", page.Markup, StringComparison.Ordinal);
            Assert.Equal("Current unsaved workflow", TitleInput(page).GetAttribute("value"));
            await FindButton(page, "Publish").ClickAsync(new());
            Assert.Equal(1, client.DryRunCalls + client.PublishCalls);
        }
        finally
        {
            history.TrySetResult(StudioWorkflowRunHistory.Empty);
            save.TrySetResult(new() { ContentItemId = "current", VersionId = "current-version" });
            await earlierCommand;
            await currentCommand;
        }

        Assert.False(TitleInput(page).HasAttribute("disabled"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.DoesNotContain("earlier-completed", page.Markup, StringComparison.Ordinal);
    }

    private static void AssertWorkflowInputsDisabled(IRenderedComponent<StudioWorkflowEditorPage> page)
    {
        var inputs = page.FindAll(".workflow-page input, .workflow-page textarea, .workflow-page select");
        Assert.NotEmpty(inputs);
        Assert.All(inputs, input => Assert.True(input.HasAttribute("disabled"), input.OuterHtml));
    }

    private static BunitContext EditorContext(RecordingWorkflowPackageClient client, ConsolePresentationMode presentation)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);
        OpenWorkflowEditorContext(ctx, presentation);
        return ctx;
    }

    private static ConsoleAgentContextRegistry OpenWorkflowEditorContext(Bunit.BunitContext ctx, ConsolePresentationMode presentation)
    {
        var registry = new ConsoleAgentContextRegistry();
        ctx.Services.AddSingleton(registry);
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = presentation == ConsolePresentationMode.V4 });
        var navigation = ctx.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo("http://localhost/console/studio/workflows/pkg-1?view=graph");
        return registry;
    }

    private static IElement TitleInput(IRenderedComponent<StudioWorkflowEditorPage> page) =>
        page.Find("section[aria-label='Workflow metadata'] input.console-input");

    private static IElement FindButton(IRenderedComponent<StudioWorkflowEditorPage> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    // This double pairs a prior valid version with a failed server validation so the test can prove a blocked save does not publish the stale version.
    private sealed class RecordingWorkflowPackageClient : IStudioWorkflowPackageClient
    {
        public const string PackageId = "pkg-1";

        public StudioWorkflowPackageDraft Draft { get; } = CreateBoundDraft();

        public IReadOnlyList<StudioWorkflowNodeDefinition> NodeDefinitions { get; init; } = [];

        public Task<StudioWorkflowEditorContext>? PendingLoad { get; init; }
        public Task<StudioWorkflowSaveResult>? PendingSave { get; init; }
        public Task<StudioWorkflowRunHistory>? PendingHistory { get; init; }
        public Task<StudioWorkflowDryRunResult>? PendingRun { get; init; }
        public Task<StudioWorkflowPublishResult>? PendingPublish { get; init; }

        public Func<StudioWorkflowPackageDraft, Task<StudioWorkflowSaveResult>>? SaveHandler { get; init; }

        public Func<string, Task<StudioWorkflowRunHistory>>? HistoryHandler { get; set; }

        public int SaveCalls { get; private set; }

        public int PublishCalls { get; private set; }

        public int DryRunCalls { get; private set; }

        private static StudioWorkflowPackageDraft CreateBoundDraft() => new()
        {
            DraftId = PackageId,
            ContentItemId = PackageId,
            CurrentVersionId = $"{PackageId}:v1",
            VersionNumber = 1,
            Title = "Parcel normalizer",
            Summary = "Nightly parcel normalizer",
            SchemaVersion = StudioWorkflowContractValues.SchemaVersion,
            LastSavedAt = DateTimeOffset.UtcNow
        };

        public Task<StudioWorkflowEditorContext> OpenEditorAsync(
            string? draftId,
            CancellationToken cancellationToken = default) =>
            draftId == "earlier" && PendingLoad is not null
                ? PendingLoad
                : Task.FromResult(new StudioWorkflowEditorContext(null, NodeDefinitions,
                    draftId is "earlier" or "current"
                        ? new StudioWorkflowPackageDraft
                        {
                            DraftId = draftId,
                            ContentItemId = draftId,
                            Title = draftId,
                            CurrentVersionId = "v1",
                            Nodes =
                            [
                                new() { Id = "source", Category = StudioWorkflowContractValues.NodeCategorySource },
                                new() { Id = "sink", Category = StudioWorkflowContractValues.NodeCategorySink }
                            ],
                            Edges = [new() { FromNodeId = "source", ToNodeId = "sink" }]
                        }
                        : Draft));

        public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>([]);

        public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowNodeDefinition>>([]);

        public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Draft);

        public Task<StudioWorkflowPackageDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowPackageDraft?>(Draft);

        // A failed graph validation: validation issues with no version id and no binding state - the same
        // shape ServerStudioWorkflowPackageClient returns on a 400 from POST .../versions.
        public Task<StudioWorkflowSaveResult> SaveVersionAsync(
            StudioWorkflowPackageDraft draft,
            string changeNote,
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            if (SaveHandler is not null) return SaveHandler(draft);
            if (PendingSave is not null) return PendingSave;
            return Task.FromResult(new StudioWorkflowSaveResult
            {
                ContentItemId = PackageId,
                ValidationIssues =
                [
                    new StudioWorkflowValidationIssue
                    {
                        Severity = "error",
                        Scope = "graph",
                        Message = "Workflow package requires at least one sink node."
                    }
                ]
            });
        }

        public Task<StudioWorkflowDryRunResult> DryRunAsync(
            StudioWorkflowPackageDraft draft,
            CancellationToken cancellationToken = default)
        {
            DryRunCalls++;
            if (PendingRun is not null) return PendingRun;
            return Task.FromResult(new StudioWorkflowDryRunResult { Status = "succeeded" });
        }

        // Correct gating never reaches this after a blocked save; returning a "queued" publish makes a
        // regression (a stale publish) fail loudly rather than pass silently.
        public Task<StudioWorkflowPublishResult> PublishAsync(
            StudioWorkflowPackageDraft draft,
            CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            if (PendingPublish is not null) return PendingPublish;
            return Task.FromResult(new StudioWorkflowPublishResult
            {
                PublicationId = "pub-stale",
                Status = "queued",
                Mode = draft.PublicationIntent.Mode
            });
        }

        public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowJobEvidence?>(null);

        public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(string contentItemId, CancellationToken cancellationToken = default) =>
            HistoryHandler is not null ? HistoryHandler(contentItemId) :
            contentItemId == "earlier" && PendingHistory is not null
                ? PendingHistory
                : Task.FromResult(StudioWorkflowRunHistory.Empty);

    }
}
