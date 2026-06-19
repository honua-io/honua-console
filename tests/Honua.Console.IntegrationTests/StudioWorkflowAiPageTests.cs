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
/// → StudioWorkflowAI). Verifies the design landmarks, the real server-bound generation flow (prompt →
/// proposed graph, structured clarification → answer → graph), the provider selector, the honest
/// AI-unavailable state, and the shared missing-binding surface (charter §11) — never seeded/mock workflow
/// data on a shipped surface. The AI-enabled paths use a dedicated test double standing in for a bound
/// honua-server with the generation contract.
/// </summary>
public sealed class StudioWorkflowAiPageTests
{
    [Fact]
    public void Page_RendersDesignLandmarks_ConversationReadinessAndDagPreview()
    {
        var page = RenderWith(new FakeAiWorkflowClient());

        // Conversation column: the shared StudioAiConversation pane titled "Workflow from prompt" with a
        // refine footer and the provider selector (two providers configured).
        var pane = page.Find("[data-studio-ai-pane]");
        Assert.Contains("Workflow from prompt", pane.QuerySelector(".studio-ai-conversation-title")!.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(pane.QuerySelectorAll(".studio-ai-refine-input"));
        Assert.NotEmpty(pane.QuerySelectorAll("[data-workflow-ai-provider]"));

        // Preview header: the workflow name input, draft badge, and the three primary actions.
        var header = page.Find(".studio-ai-preview-head");
        Assert.NotEmpty(header.QuerySelectorAll("input.workflow-title-input"));
        Assert.Contains("draft", header.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Open editor", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Run once", StringComparison.Ordinal));
        Assert.Contains(header.QuerySelectorAll("button"), b => b.TextContent.Contains("Publish", StringComparison.Ordinal));

        // Readiness summary surfaces the corrected labels (Retry policy, not the old "Evidence retention").
        var readiness = page.Find("section[aria-label='Workflow readiness summary']");
        var readinessText = readiness.TextContent;
        foreach (var label in new[] { "Steps", "Estimated duration", "Trigger", "On failure", "Retry policy" })
        {
            Assert.Contains(label, readinessText, StringComparison.Ordinal);
        }

        // DAG preview renders the live workflow graph; before a prompt it shows the honest empty state
        // (no nodes to graph) rather than a fabricated diagram.
        var dag = page.Find("section[aria-label='Workflow DAG preview'] [data-workflow-graph]");
        Assert.NotEmpty(dag.QuerySelectorAll("[data-workflow-graph-empty]"));
        Assert.Empty(dag.QuerySelectorAll("[data-workflow-graph-node]"));
    }

    [Fact]
    public void Prompt_GeneratesServerProposedGraph_AndEchoesReadyTurn()
    {
        var page = RenderWith(new FakeAiWorkflowClient());

        Assert.Empty(page.FindAll("section[aria-label='Workflow DAG preview'] [data-workflow-graph-node]"));

        page.Find(".studio-ai-refine-input").Input("Nightly: pull CSV, validate, publish to FeatureServer");
        FindButton(page, "Send").Click();

        // The server-proposed graph is applied (nodes appear) and Honua acknowledges with a ready turn.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Workflow DAG preview'] [data-workflow-graph-node]")),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(page.FindAll(".studio-ai-turn-honua .studio-ai-turn-tone-ready"));
        var log = page.Find(".studio-ai-conversation-log");
        Assert.Contains("Nightly: pull CSV", log.TextContent, StringComparison.Ordinal);

        // The failure-mode token is rendered as a readable phrase, never the raw "route-failure-edges".
        var readiness = page.Find("section[aria-label='Workflow readiness summary']").TextContent;
        Assert.DoesNotContain("route-failure-edges", readiness, StringComparison.Ordinal);
        Assert.Contains("route to failure steps", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void AmbiguousPrompt_RendersClarificationCards_ThenAnswerResumesGeneration()
    {
        var page = RenderWith(new FakeAiWorkflowClient());

        page.Find(".studio-ai-refine-input").Input("publish to the public works service");
        FindButton(page, "Send").Click();

        // A needs-clarification turn renders the structured choice cards instead of a graph.
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll(".studio-ai-clarification-card")),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll("section[aria-label='Workflow DAG preview'] [data-workflow-graph-node]"));

        // Selecting a choice answers the clarification and resumes generation → the graph appears.
        page.FindAll(".studio-ai-clarification-choice").First().Click();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("section[aria-label='Workflow DAG preview'] [data-workflow-graph-node]")),
            TimeSpan.FromSeconds(5));
        Assert.Empty(page.FindAll(".studio-ai-clarification-card"));
    }

    [Fact]
    public void AiUnavailable_RendersHonestUnavailableState_NotAFabricatedGraph()
    {
        // A server that has the workflow API but no generation provider configured.
        var page = RenderWith(new FakeAiWorkflowClient { GenerationEnabled = false });

        Assert.NotEmpty(page.FindAll("[data-workflow-ai-unavailable]"));
        Assert.Contains("AI generation unavailable", page.Markup, StringComparison.Ordinal);

        // The chat is disabled (no fabricated proposal) but the editor handoff stays available.
        Assert.True(page.Find(".studio-ai-refine-input").HasAttribute("disabled"));
        Assert.Empty(page.FindAll("section[aria-label='Workflow DAG preview'] [data-workflow-graph-node]"));
        Assert.Empty(page.FindAll("[data-workflow-ai-provider]"));
        Assert.Contains(page.FindAll("button"), b => b.TextContent.Contains("Open editor", StringComparison.Ordinal));
    }

    [Fact]
    public void Unbound_RendersSharedMissingBindingSurface_NotSeededWorkflow()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IStudioWorkflowPackageClient>(new UnsupportedStudioWorkflowPackageClient());

        var page = ctx.Render<StudioWorkflowAiPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Workflow authoring is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        Assert.Empty(page.FindAll("section[aria-label='Workflow DAG preview']"));
        Assert.Empty(page.FindAll("section[aria-label='Workflow readiness summary']"));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
    }

    private static IRenderedComponent<StudioWorkflowAiPage> RenderWith(IStudioWorkflowPackageClient client)
    {
        var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(client);

        var page = ctx.Render<StudioWorkflowAiPage>();
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-studio-ai-pane]")),
            TimeSpan.FromSeconds(5));
        return page;
    }

    private static IElement FindButton(IRenderedComponent<StudioWorkflowAiPage> page, string label) =>
        page.FindAll("button").First(button => button.TextContent.Contains(label, StringComparison.Ordinal));

    /// <summary>
    /// Test double standing in for a bound honua-server that exposes the generation contract. It performs the
    /// same observable contract the real <see cref="ServerStudioWorkflowPackageClient"/> does — bound editor
    /// context, capability with providers, and a deterministic generate/clarify outcome — so the page's real
    /// logic is exercised without a live model. It is a test fixture, never registered in the app.
    /// </summary>
    private sealed class FakeAiWorkflowClient : IStudioWorkflowPackageClient
    {
        public bool GenerationEnabled { get; init; } = true;

        public Task<StudioWorkflowEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioWorkflowEditorContext(BindingState: null, NodeDefinitions: [], Draft: NewDraft()));

        public Task<StudioWorkflowAiCapability> GetGenerationCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GenerationEnabled
                ? new StudioWorkflowAiCapability
                {
                    Enabled = true,
                    DefaultProvider = "local",
                    Providers =
                    [
                        new StudioWorkflowAiProvider("local", "Local GIS model", "local", true, "honua-gis"),
                        new StudioWorkflowAiProvider("anthropic", "Claude", "anthropic", true, "claude")
                    ]
                }
                : StudioWorkflowAiCapability.Off);

        public Task<StudioWorkflowGenerationOutcome> GenerateAsync(
            StudioWorkflowPackageDraft currentDraft,
            StudioWorkflowGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            // Answering a clarification, or an unambiguous prompt, yields a graph; an ambiguous prompt asks.
            var ambiguous = request.Answers.Count == 0 &&
                request.Prompt.Contains("public works", StringComparison.OrdinalIgnoreCase);

            if (ambiguous)
            {
                return Task.FromResult(new StudioWorkflowGenerationOutcome
                {
                    Status = StudioWorkflowGenerationStatuses.NeedsClarification,
                    Rationale = "Which FeatureServer should the publish step target?",
                    Clarifications =
                    [
                        new StudioConversationClarification(
                            "target-fs",
                            "Which FeatureServer should Publish target?",
                            "Two candidates matched 'public works'.",
                            [
                                new StudioConversationChoice("public-works-fs", "public-works-fs", "Publish layer 0"),
                                new StudioConversationChoice("pw-staging-fs", "pw-staging-fs", "Publish to staging")
                            ])
                    ]
                });
            }

            currentDraft.Nodes =
            [
                new StudioWorkflowNode { Id = "trg", Type = "trigger.cron", Category = StudioWorkflowContractValues.NodeCategorySource, Label = "Trigger", Column = 1, Row = 1 },
                new StudioWorkflowNode { Id = "tx", Type = "transform.validate", Category = StudioWorkflowContractValues.NodeCategoryTransform, Label = "Validate", Column = 2, Row = 1 },
                new StudioWorkflowNode { Id = "pub", Type = "sink.publish", Category = StudioWorkflowContractValues.NodeCategorySink, Label = "Publish", Column = 3, Row = 1 }
            ];
            currentDraft.Edges =
            [
                new StudioWorkflowEdge { Id = "e1", FromNodeId = "trg", ToNodeId = "tx", Kind = StudioWorkflowContractValues.EdgeKindSuccess },
                new StudioWorkflowEdge { Id = "e2", FromNodeId = "tx", ToNodeId = "pub", Kind = StudioWorkflowContractValues.EdgeKindSuccess }
            ];
            currentDraft.Schedule = new StudioWorkflowSchedule { Mode = "cron", Cron = "0 2 * * *", TimeZone = "UTC" };

            return Task.FromResult(new StudioWorkflowGenerationOutcome
            {
                Status = StudioWorkflowGenerationStatuses.Generated,
                Draft = currentDraft,
                Rationale = "Proposed a 3-step nightly pipeline: trigger → validate → publish.",
                Provider = request.Provider ?? "local",
                Model = "honua-gis"
            });
        }

        public Task<StudioWorkflowSaveResult> SaveVersionAsync(StudioWorkflowPackageDraft draft, string changeNote, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioWorkflowSaveResult { ContentItemId = "pkg-1", VersionId = "pkg-1:v1", VersionNumber = 1 });

        public Task<StudioWorkflowDryRunResult> DryRunAsync(StudioWorkflowPackageDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioWorkflowDryRunResult { Status = "succeeded" });

        public Task<StudioWorkflowPublishResult> PublishAsync(StudioWorkflowPackageDraft draft, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudioWorkflowPublishResult { Status = "published", PublicationId = "pub-1" });

        public Task<IReadOnlyList<StudioWorkflowDraftSummary>> ListDraftsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowDraftSummary>>([]);

        public Task<IReadOnlyList<StudioWorkflowNodeDefinition>> ListNodeDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudioWorkflowNodeDefinition>>([]);

        public Task<StudioWorkflowPackageDraft> CreateDraftAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(NewDraft());

        public Task<StudioWorkflowPackageDraft?> GetDraftAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowPackageDraft?>(NewDraft());

        public Task<StudioWorkflowJobEvidence?> GetJobEvidenceAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudioWorkflowJobEvidence?>(null);

        public Task<StudioWorkflowRunHistory> ListRunHistoryAsync(string contentItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StudioWorkflowRunHistory.Empty);

        public Task RecordGenerationFeedbackAsync(string feedbackId, string action, StudioWorkflowPackageDraft? finalDraft, string? note = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private static StudioWorkflowPackageDraft NewDraft() => new()
        {
            Title = "Untitled workflow package",
            Schedule = new StudioWorkflowSchedule { Mode = "manual" }
        };
    }
}
