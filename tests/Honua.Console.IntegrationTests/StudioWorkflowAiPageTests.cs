using AngleSharp.Dom;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the Studio "Workflow from prompt" conversational entry
/// (<see cref="StudioWorkflowAiPage"/> at <c>/studio/workflows/new</c>, per screens-studio-form-workflow.jsx
/// → StudioWorkflowAI). Verifies the design landmarks (conversation pane, readiness summary, DAG preview, and
/// the Open editor / Run once / Publish header), the server-bound refine/run/publish flow, and the shared
/// missing-binding surface (charter §11) — never seeded/mock workflow data.
/// </summary>
public sealed class StudioWorkflowAiPageTests
{
    [Fact]
    public void Page_RendersDesignLandmarks_ConversationReadinessAndDagPreview()
    {
        var page = RenderBound(out _);

        // Conversation column: the shared StudioAiConversation pane titled "Workflow from prompt", opening
        // with a Honua "ready" readiness turn plus a refine footer.
        var pane = page.Find("[data-studio-ai-pane]");
        Assert.Contains("Workflow from prompt", pane.QuerySelector(".studio-ai-conversation-title")!.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(pane.QuerySelectorAll(".studio-ai-turn-honua .studio-ai-turn-tone-ready"));
        Assert.NotEmpty(pane.QuerySelectorAll(".studio-ai-refine-input"));

        // Preview header: the workflow name input, draft + DAG-valid badges, and the three primary actions.
        var header = page.Find(".studio-ai-preview-head");
        Assert.NotEmpty(header.QuerySelectorAll("input.workflow-title-input"));
        Assert.Contains("draft", header.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DAG", header.TextContent, StringComparison.Ordinal);
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Open editor", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Run once", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Publish", StringComparison.Ordinal));

        // Readiness summary surfaces steps / duration / trigger / failure / retention from the live draft.
        var readiness = page.Find("section[aria-label='Workflow readiness summary']");
        var readinessText = readiness.TextContent;
        foreach (var label in new[] { "Steps", "Estimated duration", "Trigger", "On failure", "Evidence retention" })
        {
            Assert.Contains(label, readinessText, StringComparison.Ordinal);
        }
        Assert.NotEmpty(readiness.QuerySelectorAll(".workflow-ai-duration-chip"));

        // DAG preview draws the SVG edge layer (arrow markers + failure marker) over positioned nodes.
        var dag = page.Find("section[aria-label='Workflow DAG preview'] .workflow-canvas");
        Assert.NotEmpty(dag.QuerySelectorAll("svg.workflow-canvas-edges"));
        Assert.NotEmpty(dag.QuerySelectorAll("marker#workflow-ai-arrow"));
        Assert.NotEmpty(dag.QuerySelectorAll("marker#workflow-ai-arrow-failure"));
        Assert.NotEmpty(dag.QuerySelectorAll(".workflow-canvas-nodes .workflow-node"));
        Assert.NotEmpty(dag.QuerySelectorAll(".workflow-canvas-edge"));
    }

    [Fact]
    public void Refine_AppendsStepToDraftGraph_AndEchoesTurns()
    {
        var page = RenderBound(out _);

        var nodesBefore = page.FindAll("section[aria-label='Workflow DAG preview'] .workflow-node").Count;

        page.Find(".studio-ai-refine-input").Input("Add a dedupe step before publish");
        FindButton(page, "Send").Click();

        page.WaitForAssertion(
            () => Assert.True(
                page.FindAll("section[aria-label='Workflow DAG preview'] .workflow-node").Count > nodesBefore),
            TimeSpan.FromSeconds(5));

        // The conversation echoes the author's refine turn and a Honua acknowledgement.
        var log = page.Find(".studio-ai-conversation-log");
        Assert.Contains("Add a dedupe step before publish", log.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll(".studio-ai-turn-you"));
    }

    [Fact]
    public void OpenEditor_SavesDraft_ThenNavigatesIntoTheEditorRoute()
    {
        var page = RenderBound(out _);
        var nav = page.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        FindButton(page, "Open editor").Click();

        // The persisted draft (with its new content-item id) hands off to the existing editor route, off "new".
        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("/studio/workflows/", nav.Uri, StringComparison.Ordinal);
                Assert.DoesNotContain("/studio/workflows/new", nav.Uri, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RunOnce_RendersSampleEvidence_FromServerDryRun()
    {
        var page = RenderBound(out _);

        FindButton(page, "Run once").Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Run once result']")),
            TimeSpan.FromSeconds(5));
        Assert.Contains("succeeded", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unbound_RendersSharedMissingBindingSurface_NotSeededWorkflow()
    {
        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.RenderComponent<StudioWorkflowAiPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Workflow authoring is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // No DAG preview / readiness summary is fabricated when unbound.
        Assert.Empty(page.FindAll("section[aria-label='Workflow DAG preview']"));
        Assert.Empty(page.FindAll("section[aria-label='Workflow readiness summary']"));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<StudioWorkflowAiPage> RenderBound(out InMemoryStudioWorkflowPackageClient client)
    {
        client = InMemoryStudioWorkflowPackageClient.CreateSeeded();
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(client);

        var page = ctx.RenderComponent<StudioWorkflowAiPage>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-ai-pane]")),
            TimeSpan.FromSeconds(5));
        return page;
    }

    private static IElement FindButton(IRenderedComponent<StudioWorkflowAiPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));
}
