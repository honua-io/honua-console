using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the omni-prompt AI console (honua-console#203). One prompt box routes to BOTH
/// lanes: a Studio (GIS authoring) prompt lands on the unified data→publish flow in AI mode (the #200 outcome
/// card path); a DevOps (ops) prompt lands on the deploy approval queue (the #197 approval surface). The
/// approval gate is preserved in both lanes — nothing actuates from the prompt. The keyword classifier NEVER
/// silently routes: every verdict — high-confidence or ambiguous — surfaces a confirmable suggestion chip the
/// human accepts with one click before the lane mounts.
/// </summary>
public sealed class OmniPromptConsolePageRenderTests
{
    [Fact]
    public void OmniPrompt_RendersSinglePromptComposer_WithNoWorkflowTypePicker()
    {
        using var ctx = NewContext();

        var page = ctx.Render<OmniPromptConsolePage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-composer]"));
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-input]"));
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-submit]"));
                // The single prompt replaces any workflow-type picker; no type-grid is present here.
                Assert.Empty(page.FindAll("[data-content-type]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OmniPrompt_StudioIntent_SuggestsStudioLane_ThenRoutesOnConfirm()
    {
        using var ctx = NewContext();

        var page = ctx.Render<OmniPromptConsolePage>();

        page.Find("[data-omni-prompt-input]").Input("publish Maui parcels as a feature service");
        page.Find("[data-omni-prompt-submit]").Click();

        // A confident Studio classify surfaces a "Best guess" suggestion chip — it does NOT silently route.
        page.WaitForAssertion(
            () =>
            {
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-confirm]"));
                Assert.Contains("Best guess", page.Markup, StringComparison.Ordinal);
                Assert.NotEmpty(page.FindAll("[data-omni-confirm-studio]"));
                // No lane is committed until the human confirms.
                Assert.Empty(page.FindAll("[data-data-publish-flow]"));
                Assert.Empty(page.FindAll("[data-omni-devops-surface]"));
            },
            TimeSpan.FromSeconds(5));

        // Confirming the suggested Studio lane mounts the data→publish flow's AI driver surface.
        page.Find("[data-omni-confirm-studio]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Routed to", page.Markup, StringComparison.Ordinal);
                Assert.NotEmpty(page.FindAll("[data-data-publish-flow]"));
                Assert.NotEmpty(page.FindAll("[data-ai-driver]"));
                Assert.Empty(page.FindAll("[data-omni-devops-surface]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OmniPrompt_StudioLane_SeedsTheAiIntentBoxFromThePrompt()
    {
        // The AI intent textarea only renders when the server reports AI generation is available, so this test
        // binds a capability-enabled driver. The intent box must be pre-filled with the omni-prompt text.
        using var ctx = NewContext(aiDriver: new CapableAiPublishDriver());

        var page = ctx.Render<OmniPromptConsolePage>();

        const string intent = "publish Maui parcels as a feature service";
        page.Find("[data-omni-prompt-input]").Input(intent);
        page.Find("[data-omni-prompt-submit]").Click();

        // Confirm the suggested Studio lane before the flow mounts (no silent route).
        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-omni-confirm-studio]")),
            TimeSpan.FromSeconds(5));
        page.Find("[data-omni-confirm-studio]").Click();

        page.WaitForAssertion(
            () =>
            {
                // The flow's AI intent textarea renders (AI is available) and is pre-filled with the omni-prompt
                // text — the operator continues in the Studio AI lane without retyping.
                var aiDriver = page.Find("[data-ai-driver]");
                Assert.NotNull(aiDriver.QuerySelector("[data-ai-intent]"));
                Assert.Contains(intent, aiDriver.InnerHtml, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OmniPrompt_DevOpsIntent_SuggestsDevOpsLane_ThenRoutesToDeployApprovalQueueOnConfirm_PreservingTheGate()
    {
        using var ctx = NewContext();

        var page = ctx.Render<OmniPromptConsolePage>();

        page.Find("[data-omni-prompt-input]").Input("roll back staging to the last good revision");
        page.Find("[data-omni-prompt-submit]").Click();

        // A confident DevOps classify surfaces a "Best guess" suggestion chip — it does NOT silently route.
        page.WaitForAssertion(
            () =>
            {
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-confirm]"));
                Assert.Contains("Best guess", page.Markup, StringComparison.Ordinal);
                Assert.NotEmpty(page.FindAll("[data-omni-confirm-devops]"));
                Assert.Empty(page.FindAll("[data-omni-devops-surface]"));
            },
            TimeSpan.FromSeconds(5));

        // Confirming the suggested DevOps lane mounts the deploy approval queue (the human-in-the-loop gate).
        page.Find("[data-omni-confirm-devops]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Routed to", page.Markup, StringComparison.Ordinal);
                Assert.NotEmpty(page.FindAll("[data-omni-devops-surface]"));
                Assert.Contains("Deploy approvals", page.Markup, StringComparison.Ordinal);
                // The Studio publish flow is NOT shown for a DevOps prompt.
                Assert.Empty(page.FindAll("[data-data-publish-flow]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OmniPrompt_LowConfidencePrompt_ShowsConfirmChipInsteadOfMisrouting()
    {
        using var ctx = NewContext();

        var page = ctx.Render<OmniPromptConsolePage>();

        // "publish" + "rollback" fires both lanes evenly → ambiguous → confirm chip, no lane committed yet.
        page.Find("[data-omni-prompt-input]").Input("publish then rollback");
        page.Find("[data-omni-prompt-submit]").Click();

        page.WaitForAssertion(
            () =>
            {
                Assert.NotEmpty(page.FindAll("[data-omni-prompt-confirm]"));
                Assert.NotEmpty(page.FindAll("[data-omni-confirm-studio]"));
                Assert.NotEmpty(page.FindAll("[data-omni-confirm-devops]"));
                // Neither lane surface is committed until the human picks.
                Assert.Empty(page.FindAll("[data-data-publish-flow]"));
                Assert.Empty(page.FindAll("[data-omni-devops-surface]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void OmniPrompt_ConfirmChip_CommitsTheChosenLane()
    {
        using var ctx = NewContext();

        var page = ctx.Render<OmniPromptConsolePage>();

        page.Find("[data-omni-prompt-input]").Input("publish then rollback");
        page.Find("[data-omni-prompt-submit]").Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-omni-confirm-devops]")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-omni-confirm-devops]").Click();

        page.WaitForAssertion(
            () => Assert.NotEmpty(page.FindAll("[data-omni-devops-surface]")),
            TimeSpan.FromSeconds(5));
    }

    private static BunitContext NewContext(IAiPublishDriver? aiDriver = null)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton<IOmniPromptIntentClassifier>(new OmniPromptIntentClassifier());

        // DevOps lane backends — no operations to approve in the render harness (honest empty queue).
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(new StubReleaseClient());
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());

        // Studio lane backend — the unified data→publish flow's dependencies. The Unsupported impls render the
        // honest missing-binding / AI-unavailable posture (Charter §11); the AI driver surface still mounts.
        ctx.Services.AddSingleton<IConsoleFileImportOperation>(new UnsupportedConsoleFileImportOperation());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());
        ctx.Services.AddSingleton<IStudioMapStyleCatalogDataSource>(new UnsupportedStudioMapStyleCatalogDataSource());
        ctx.Services.AddSingleton<IAiPublishDriver>(aiDriver ?? new UnsupportedAiPublishDriver());

        return ctx;
    }

    // A capability-enabled AI driver so the data→publish flow renders its intent textarea (the Unsupported
    // driver hides it behind the "AI unavailable" panel). It proposes nothing here — the seed is what matters.
    private sealed class CapableAiPublishDriver : IAiPublishDriver
    {
        public Task<AiPublishCapability> GetCapabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiPublishCapability(Enabled: true, DefaultProvider: "bedrock", Detail: null));

        public Task<AiPublishOutcome> ProposeAsync(
            string intent,
            IReadOnlyList<AiPublishResourceRef> knownResources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AiPublishOutcome.NeedsInput("Pick a resource.", []));

        public Task RecordDecisionAsync(string? feedbackId, string action, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    // A release client that reports no list endpoint (the server's honest posture) → the deploy-approval queue
    // projects from no operations and renders the empty state. The DevOps lane still mounts.
    private sealed class StubReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([]));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(OperateSectionStatus.Missing, "n/a"));
    }
}
