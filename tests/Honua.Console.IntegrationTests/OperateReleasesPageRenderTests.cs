using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the GitOps metadata release visualization surface.
/// The surface binds to a real honua-server (charter section 11) and never to a standing
/// mock. The list route honestly reports that the server has no release-package list
/// endpoint; the by-id detail route renders the proposal summary, semantic diff, the
/// environment matrix/drift, the CI/GitOps timeline, and rollback readiness. Blockers
/// (proposal findings or operation-lifecycle blockers) disable the Git PR action.
/// </summary>
public sealed class OperateReleasesPageRenderTests
{
    [Fact]
    public void ReleaseList_ReportsNoServerListEndpoint()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unsupported,
                "The connected honua-server addresses GitOps metadata release packages by id and does " +
                "not expose a release-package list endpoint."),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Unsupported by this server", page.Markup, StringComparison.Ordinal);
                Assert.Contains("does not expose a release-package list endpoint", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDetail_WhenServerContractNotBound_RendersMissingBindingSurface()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Unsupported,
                "The connected honua-server does not yet expose the GitOps metadata release package contract."),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

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
    public void ReleaseDetail_WhenServerReturnsReadyRelease_RendersDiffMatrixTimelineAndRollback()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(BuildDetail(blocked: false)),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                // Header title row with the design's status badge (preflight, not applied/blocked).
                Assert.Contains("Promote parcels", page.Markup, StringComparison.Ordinal);
                var badge = page.Find(".operate-release-title-row .console-status");
                Assert.Equal("preflight", badge.TextContent.Trim());
                // Design section tab strip is present for the major views.
                var tabs = page.FindAll(".operate-release-tabs a");
                Assert.Equal(5, tabs.Count);
                Assert.Contains(tabs, t => t.TextContent.Contains("Semantic diff", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Environment matrix", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("CI timeline", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Rollback", StringComparison.Ordinal));
                // Two-column detail grid with a main column and a context side rail.
                Assert.NotNull(page.Find(".operate-release-grid .operate-release-main"));
                Assert.NotNull(page.Find(".operate-release-grid .operate-release-side"));
                // Semantic diff table region.
                Assert.NotNull(page.Find("#semantic-diff table.operate-diff-table"));
                Assert.Contains("field contract", page.Markup, StringComparison.Ordinal);
                // Environment matrix / drift state.
                Assert.NotNull(page.Find("#env-matrix table.operate-env-matrix"));
                Assert.Contains("Environment matrix and drift", page.Markup, StringComparison.Ordinal);
                Assert.Contains("behind", page.Markup, StringComparison.Ordinal);
                Assert.Contains("drift detected", page.Markup, StringComparison.Ordinal);
                Assert.NotNull(page.Find("#env-matrix tr.operate-drift-row"));
                // CI/GitOps check timeline shows the same release operation id as server/devops.
                Assert.NotNull(page.Find("#timeline ol.operate-timeline"));
                Assert.Contains("op-9", page.Markup, StringComparison.Ordinal);
                Assert.Contains("CI/GitOps", page.Markup, StringComparison.Ordinal);
                // Git PR preview link is offered (header + side rail).
                Assert.Contains("https://git.example/pr/9", page.Markup, StringComparison.Ordinal);
                // Compatibility preflight summary region (design view).
                Assert.NotNull(page.Find("#preflight"));
                // "What proceed does" governed-step explainer (shown for a non-applied release).
                Assert.Contains("What proceed does", page.Markup, StringComparison.Ordinal);
                // Rollback readiness + window before apply.
                Assert.NotNull(page.Find("#rollback"));
                Assert.Contains("Rollback readiness", page.Markup, StringComparison.Ordinal);
                Assert.Contains("snapshot-required", page.Markup, StringComparison.Ordinal);
                Assert.Contains("restore parcels snapshot", page.Markup, StringComparison.Ordinal);
                // Governed-operation note (design's red annotation).
                Assert.Contains("Governed operation", page.Markup, StringComparison.Ordinal);
                // Ready proposal is not blocked.
                Assert.Contains("console-state-success", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDetail_WhenOperationHasBlockers_DisablesPullRequestActionWithReason()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(BuildDetail(blocked: true)),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("console-state-danger", page.Markup, StringComparison.Ordinal);
                // The header status badge reads blocked.
                var badge = page.Find(".operate-release-title-row .console-status");
                Assert.Equal("blocked", badge.TextContent.Trim());
                // The breaking-change callout makes the blocker impossible to miss.
                Assert.Contains("Blocking findings", page.Markup, StringComparison.Ordinal);
                // Blockers prevent the PR/deploy action (acceptance criterion).
                Assert.Contains("Resolve blocking findings before creating a Git PR.", page.Markup, StringComparison.Ordinal);
                Assert.Contains("smoke SLO burn exceeded budget", page.Markup, StringComparison.Ordinal);
                // The disabled proceed action button is rendered.
                var button = page.Find("button.console-button");
                Assert.True(button.HasAttribute("disabled"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDetail_WhenOperationNotStarted_RendersMissingOperationStateButKeepsProposal()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(
                BuildDetail(blocked: false) with
                {
                    Operation = null,
                    OperationStatus = OperateSectionStatus.Missing,
                    OperationMessage = "No release operation has been started for this package yet.",
                }),
        };

        using var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);

        var page = ctx.RenderComponent<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Promote parcels", page.Markup, StringComparison.Ordinal);
                Assert.Contains("No release operation has been started", page.Markup, StringComparison.Ordinal);
                // The matrix still renders even without an operation.
                Assert.Contains("Environment matrix and drift", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static GitOpsReleaseDetail BuildDetail(bool blocked)
    {
        var rollback = new GitOpsRollbackPlan(
            GitOpsRollbackClassification.SnapshotRequired,
            IsDataAffecting: true,
            RequiresExplicitApproval: true,
            Steps: ["restore parcels snapshot"],
            EvidenceRequired: ["backup-id"],
            ApprovalPolicyRef: "policy/rollback-prod");

        var operation = new GitOpsReleaseOperation(
            OperationId: "op-9",
            Status: blocked ? "failed" : "running",
            CurrentStage: blocked ? "ci-failed" : "ci",
            PrUrl: "https://git.example/pr/9",
            CommitSha: "abc123def456",
            GitOperationId: "git-op-9",
            DeployOperationId: null,
            TargetEnvironment: "prod",
            DesiredRevision: "77",
            Timeline:
            [
                new GitOpsTimelineStep("Git PR", GitOpsTimelineStatus.Succeeded, "https://git.example/pr/9"),
                new GitOpsTimelineStep("CI/GitOps", blocked ? GitOpsTimelineStatus.Failed : GitOpsTimelineStatus.Running, "ci"),
            ],
            Blockers: blocked ? ["smoke SLO burn exceeded budget"] : [],
            Warnings: [],
            RollbackPlan: rollback);

        var proposal = new GitOpsReleaseProposal(
            ReleasePackageId: "11111111-2222-3333-4444-555555555555",
            Title: "Promote parcels",
            Summary: "Promote the parcels field contract change to prod.",
            SourceEnvironmentId: "staging",
            TargetEnvironmentIds: ["prod"],
            DesiredRevision: "rev-77",
            ChangedResources:
            [
                new GitOpsChangedResource(
                    "field:parcels.zoning",
                    "field",
                    "field:parcels.zoning",
                    [GitOpsChangeClass.FieldContract]),
            ],
            RollbackClassification: GitOpsRollbackClassification.SnapshotRequired,
            HasBlockingFindings: blocked);

        var matrix = new[]
        {
            new GitOpsEnvironmentMatrixCell("prod", GitOpsTargetBindingState.Bound, 77, 70),
        };

        return new GitOpsReleaseDetail(proposal, matrix, operation, OperateSectionStatus.Allowed, string.Empty);
    }

    private sealed class StubReleaseClient : IConsoleGitOpsReleaseClient
    {
        public OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>> Proposals { get; init; } =
            OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unsupported,
                "No release-package list endpoint.");

        public Func<string, OperateSectionResult<GitOpsReleaseDetail>> Detail { get; init; } =
            _ => OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Missing, "Release not found.");

        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Proposals);

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default)
        {
            var detail = Detail(releasePackageId);
            return Task.FromResult(detail.IsAllowed
                ? OperateSectionResult<GitOpsReleaseProposal>.Allowed(detail.Value!.Proposal)
                : OperateSectionResult<GitOpsReleaseProposal>.Denied(detail.Status, detail.Message));
        }

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail(releasePackageId));
    }
}
