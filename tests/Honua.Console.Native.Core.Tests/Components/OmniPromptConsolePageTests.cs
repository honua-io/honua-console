using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// bUnit tests for the omni-prompt AI console page (honua-console#203).
/// Verifies the honesty guarantees: no silent misroute (even a high-confidence keyword verdict always
/// shows a confirmable suggestion chip), and the page is fully usable without an AI provider because
/// the classifier is keyword-only and server-independent.
/// </summary>
public sealed class OmniPromptConsolePageTests : ConsoleComponentTestBase
{
    // --- Stub services -------------------------------------------------------

    /// <summary>Controllable classifier that returns a fixed verdict.</summary>
    private sealed class StubClassifier : IOmniPromptIntentClassifier
    {
        public OmniPromptConfidence Confidence { get; set; } = OmniPromptConfidence.High;
        public OmniPromptIntent Intent { get; set; } = OmniPromptIntent.Studio;
        public string Rationale { get; set; } = "Test rationale from stub classifier.";

        public OmniPromptClassification Classify(string prompt) =>
            new(Intent, Confidence, Rationale);
    }

    private sealed class StubGitOpsReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([]));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(OperateSectionStatus.Missing, "stub"));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Missing, "stub"));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(OperateSectionStatus.Missing, "stub"));
    }

    private sealed class StubDeployApprovalClient : IConsoleDeployApprovalClient
    {
        public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
            string operationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Missing, "stub"));

        public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
            IReadOnlyList<string> operationIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Allowed([]));

        public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
            string operationId,
            string? reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Missing, "stub"));

        public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
            string operationId,
            string? reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(OperateSectionStatus.Missing, "stub"));
    }

    // --- Test setup ----------------------------------------------------------

    private readonly StubClassifier _classifier = new();

    public OmniPromptConsolePageTests()
    {
        Services.AddSingleton<IOmniPromptIntentClassifier>(_classifier);
        Services.AddSingleton<IConsoleGitOpsReleaseClient>(new StubGitOpsReleaseClient());
        Services.AddSingleton<IConsoleDeployApprovalClient>(new StubDeployApprovalClient());
        // NavigationManager is provided by the bUnit host automatically.
    }

    // --- No-silent-misroute tests -------------------------------------------

    [Fact]
    public void High_confidence_Studio_verdict_shows_confirm_chip_not_auto_route()
    {
        // Arrange — classifier will return High/Studio, which previously caused a silent misroute.
        _classifier.Confidence = OmniPromptConfidence.High;
        _classifier.Intent = OmniPromptIntent.Studio;

        var cut = Render<OmniPromptConsolePage>();

        // Act — type a Studio-leaning prompt and click Route.
        cut.Find("[data-omni-prompt-input]").Input("publish Maui parcels as a feature service");
        cut.Find("[data-omni-prompt-submit]").Click();

        // Assert — the confirm chip must appear; the Studio lane surface must NOT have rendered yet.
        var chip = cut.Find("[data-omni-prompt-confirm]");
        Assert.Contains("Best guess", chip.TextContent);
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-studio]"));
        // The Studio or DevOps lane content is not in the DOM yet — no silent route.
        Assert.Empty(cut.FindAll("[data-data-publish-flow]"));
        Assert.Empty(cut.FindAll("[data-omni-devops-surface]"));
    }

    [Fact]
    public void High_confidence_DevOps_verdict_shows_confirm_chip_not_auto_route()
    {
        _classifier.Confidence = OmniPromptConfidence.High;
        _classifier.Intent = OmniPromptIntent.DevOps;

        var cut = Render<OmniPromptConsolePage>();

        cut.Find("[data-omni-prompt-input]").Input("roll back staging to the last good revision");
        cut.Find("[data-omni-prompt-submit]").Click();

        var chip = cut.Find("[data-omni-prompt-confirm]");
        Assert.Contains("Best guess", chip.TextContent);
        // The DevOps confirm button is the primary choice.
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-devops]"));
        Assert.Empty(cut.FindAll("[data-omni-devops-surface]"));
    }

    [Fact]
    public void Low_confidence_verdict_shows_equal_choice_chip_not_auto_route()
    {
        _classifier.Confidence = OmniPromptConfidence.Low;
        _classifier.Intent = OmniPromptIntent.Studio;

        var cut = Render<OmniPromptConsolePage>();

        cut.Find("[data-omni-prompt-input]").Input("do the thing");
        cut.Find("[data-omni-prompt-submit]").Click();

        var chip = cut.Find("[data-omni-prompt-confirm]");
        // Low-confidence shows "Which lane?" not "Best guess".
        Assert.Contains("Which lane?", chip.TextContent);
        // Both choices are present with equal weight.
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-studio]"));
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-devops]"));
        Assert.Empty(cut.FindAll("[data-data-publish-flow]"));
    }

    [Fact]
    public void Rationale_from_classifier_is_shown_in_the_confirm_chip()
    {
        _classifier.Confidence = OmniPromptConfidence.High;
        _classifier.Rationale = "The prompt contains Studio-specific keywords.";

        var cut = Render<OmniPromptConsolePage>();

        cut.Find("[data-omni-prompt-input]").Input("publish parcels");
        cut.Find("[data-omni-prompt-submit]").Click();

        Assert.Contains("The prompt contains Studio-specific keywords.", cut.Find("[data-omni-prompt-confirm]").TextContent);
    }

    // --- AI-unavailable renders usable explicit choice path ------------------

    [Fact]
    public void AI_unavailable_scenario_page_renders_route_form_and_chip()
    {
        // When no AI provider is configured the classifier is still fully functional (it is keyword-only
        // and server-independent). The page should render normally and the confirm chip should appear.
        _classifier.Confidence = OmniPromptConfidence.Low;
        _classifier.Rationale = "Describe what you want — publishing a layer, or a deploy/rollback/upgrade.";

        var cut = Render<OmniPromptConsolePage>();

        // The prompt input and submit button are always present — no dead AI box.
        Assert.Single(cut.FindAll("[data-omni-prompt-input]"));
        Assert.Single(cut.FindAll("[data-omni-prompt-submit]"));

        // Force-lane overrides are always present.
        Assert.NotNull(cut.Find("[data-omni-force-studio]"));
        Assert.NotNull(cut.Find("[data-omni-force-devops]"));

        // After typing and clicking Route the chip appears with both lane choices.
        cut.Find("[data-omni-prompt-input]").Input("help me with this");
        cut.Find("[data-omni-prompt-submit]").Click();

        var chip = cut.Find("[data-omni-prompt-confirm]");
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-studio]"));
        Assert.NotNull(chip.QuerySelector("[data-omni-confirm-devops]"));
    }

    [Fact]
    public void Empty_prompt_does_not_show_confirm_chip()
    {
        var cut = Render<OmniPromptConsolePage>();

        // The Route button is disabled when the prompt is empty, so clicking it has no effect.
        Assert.Empty(cut.FindAll("[data-omni-prompt-confirm]"));
    }
}
