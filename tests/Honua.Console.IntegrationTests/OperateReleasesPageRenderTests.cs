using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the GitOps metadata release visualization surface.
/// The surface binds to a real honua-server (charter section 11) and never to a standing
/// mock, so two states must render correctly: the missing-binding surface when the server
/// release contract is not bound, and the live proposal summary when the server returns
/// release data. A deep link to an unknown release id must surface its own missing-detail
/// state rather than being swallowed.
/// </summary>
public sealed class OperateReleasesPageRenderTests
{
    [Fact]
    public void Releases_WhenServerContractNotBound_RendersMissingBindingSurface()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unsupported,
                "The connected honua-server does not yet expose the GitOps metadata release package contract."),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Unsupported by this server", page.Markup, StringComparison.Ordinal);
                Assert.Contains(
                    "does not yet expose the GitOps metadata release package contract",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Releases_WhenServerReturnsProposal_RendersSummaryAndRollbackReadiness()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
                [BuildProposal(hasBlockingFindings: false)]),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Promote parcels layer", page.Markup, StringComparison.Ordinal);
                // Rollback readiness is visible before apply (acceptance criterion).
                Assert.Contains("metadata-only", page.Markup, StringComparison.Ordinal);
                Assert.Contains("field contract", page.Markup, StringComparison.Ordinal);
                // A ready proposal shows the ready status, not blocked.
                Assert.Contains("console-state-success", page.Markup, StringComparison.Ordinal);
                Assert.DoesNotContain("console-state-danger", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Releases_WhenProposalHasBlockingFindings_DisablesPullRequestActionWithReason()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
                [BuildProposal(hasBlockingFindings: true)]),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("console-state-danger", page.Markup, StringComparison.Ordinal);
                // Blockers prevent the PR/deploy action (acceptance criterion): the button stays
                // disabled and explains why.
                Assert.Contains("Resolve blocking findings before creating a Git PR.", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDeepLink_WhenReleaseListEmpty_StillRendersMissingDetailSurface()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([]),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "rel-unknown"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("No release proposals", page.Markup, StringComparison.Ordinal);
                Assert.Contains(
                    "Release 'rel-unknown' was not found in the loaded server release list.",
                    page.Markup,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static GitOpsReleaseProposal BuildProposal(bool hasBlockingFindings) => new(
        ReleasePackageId: "rel-001",
        Title: "Promote parcels layer",
        Summary: "Field contract change tested in staging.",
        SourceEnvironmentId: "staging",
        TargetEnvironmentIds: ["prod"],
        DesiredRevision: "rev-42",
        ChangedResources:
        [
            new GitOpsChangedResource(
                "layer:parcels",
                "layer",
                "Parcels",
                [GitOpsChangeClass.FieldContract]),
        ],
        RollbackClassification: GitOpsRollbackClassification.MetadataOnly,
        HasBlockingFindings: hasBlockingFindings);

    private sealed class StubReleaseClient : IConsoleGitOpsReleaseClient
    {
        public OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>> Proposals { get; init; } =
            OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([]);

        public Func<string, OperateSectionResult<GitOpsReleaseProposal>> Proposal { get; init; } =
            _ => OperateSectionResult<GitOpsReleaseProposal>.Denied(OperateSectionStatus.Missing, "Release not found.");

        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposals);

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposal(releasePackageId));
    }
}
