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
    public void ReleaseList_WhenServerReturnsProposals_RendersList()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
            [
                new GitOpsReleaseProposal(
                    ReleasePackageId: "11111111-2222-3333-4444-555555555555",
                    Title: "Promote parcels",
                    Summary: "Promote the parcels field contract change to prod.",
                    SourceEnvironmentId: "staging",
                    TargetEnvironmentIds: ["prod"],
                    DesiredRevision: "rev-77",
                    ChangedResources: [],
                    RollbackClassification: GitOpsRollbackClassification.Unknown,
                    HasBlockingFindings: false),
            ]),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Contains("Promote parcels", page.Markup, StringComparison.Ordinal);
                Assert.Contains("staging", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseList_WhenServerListUnavailable_RendersHonestUnavailableSurface()
    {
        var stub = new StubReleaseClient
        {
            Proposals = OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                "The honua-server admin API is unreachable or returned an unreadable response."),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>();

        page.WaitForAssertion(
            () => Assert.Contains("unreachable", page.Markup, StringComparison.OrdinalIgnoreCase),
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

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
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

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                // Header title row with the design's status badge (preflight, not applied/blocked).
                Assert.Contains("Promote parcels", page.Markup, StringComparison.Ordinal);
                var badge = page.Find(".operate-release-title-row .console-status");
                Assert.Equal("preflight", badge.TextContent.Trim());
                // Design section tab strip is present for the major views, including the
                // dedicated Data scripts and Git PR preview tabs (design fidelity).
                var tabs = page.FindAll(".operate-release-tabs a");
                Assert.Equal(8, tabs.Count);
                Assert.Contains(tabs, t => t.TextContent.Contains("Semantic diff", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Environment matrix", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Data scripts", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Git PR preview", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("CI timeline", StringComparison.Ordinal));
                Assert.Contains(tabs, t => t.TextContent.Contains("Rollback", StringComparison.Ordinal));
                // Dedicated Data-scripts section with per-script covered / no-rollback badges.
                var scriptsSection = page.Find("#scripts.operate-release-scripts");
                Assert.NotNull(scriptsSection);
                Assert.Contains("Data scripts", scriptsSection.QuerySelector("#scripts-heading")!.TextContent, StringComparison.Ordinal);
                Assert.Contains("2", scriptsSection.QuerySelector("#scripts-heading")!.TextContent, StringComparison.Ordinal);
                var scriptRows = page.FindAll("#scripts .operate-script-row");
                Assert.Equal(2, scriptRows.Count);
                Assert.Contains("001_add_parcels_index.sql", page.Markup, StringComparison.Ordinal);
                Assert.Contains("002_drop_zoning_code.sql", page.Markup, StringComparison.Ordinal);
                var scriptBadges = page.FindAll("#scripts .operate-script-row .console-status");
                Assert.Contains(scriptBadges, b => b.TextContent.Trim() == "covered");
                Assert.Contains(scriptBadges, b => b.TextContent.Trim() == "no rollback");
                // A coverage gap surfaces on the section header and in the preflight summary.
                Assert.Contains("coverage gap", page.Markup, StringComparison.Ordinal);
                Assert.Contains("Data script coverage gap", page.Markup, StringComparison.Ordinal);
                // Inline Git PR-diff preview region renders the change summary in-page.
                var prSection = page.Find("#pr-preview.operate-release-pr");
                Assert.NotNull(prSection);
                Assert.NotNull(page.Find("#pr-preview ul.operate-pr-diff"));
                Assert.Contains("field:parcels.zoning", prSection.InnerHtml, StringComparison.Ordinal);
                // The inline preview keeps the external GitHub link too.
                Assert.NotNull(page.Find("#pr-preview a.operate-pr-external"));
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

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
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

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
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

    [Fact]
    public void ReleaseDetail_WhenServerReportsNoDataScripts_RendersEmptyScriptsState()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(
                BuildDetail(blocked: false) with { DataScripts = [] }),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                // The dedicated Data-scripts section still renders, in its empty state.
                Assert.NotNull(page.Find("#scripts.operate-release-scripts"));
                Assert.Contains("0", page.Find("#scripts-heading").TextContent, StringComparison.Ordinal);
                Assert.Contains("reported no data scripts for this release bundle", page.Markup, StringComparison.Ordinal);
                Assert.Empty(page.FindAll("#scripts .operate-script-row"));
                // The inline PR preview still renders the change summary.
                Assert.NotNull(page.Find("#pr-preview ul.operate-pr-diff"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDetail_WhenCoordinatedReleaseExists_RendersThreeArtifactsTimelineAndRollback()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(BuildDetail(blocked: false)),
            Coordinated = _ => OperateSectionResult<GitOpsCoordinatedRelease>.Allowed(BuildCoordinated()),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                // The coordinated section renders with the three artifact kinds side by side.
                Assert.NotNull(page.Find("#coordinated"));
                Assert.Contains("container image", page.Markup, StringComparison.Ordinal);
                Assert.Contains("DB / schema change", page.Markup, StringComparison.Ordinal);
                Assert.Contains("metadata semantic diff", page.Markup, StringComparison.Ordinal);
                // The ordered step timeline renders all coordinated steps.
                var steps = page.FindAll("#coordinated .operate-coordinated-timeline li");
                Assert.Equal(6, steps.Count);
                // The data-affecting gate is parked awaiting approval, and rollback is ready.
                Assert.Contains("Awaiting approval", page.Markup, StringComparison.Ordinal);
                Assert.Contains("unwinds completed steps in reverse", page.Markup, StringComparison.Ordinal);
                // The coordinated tab is present in the strip.
                Assert.Contains("Coordinated upgrade", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ReleaseDetail_WhenNoCoordinatedRelease_RendersMissingState()
    {
        var stub = new StubReleaseClient
        {
            Detail = _ => OperateSectionResult<GitOpsReleaseDetail>.Allowed(BuildDetail(blocked: false)),
            Coordinated = _ => OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Missing,
                "No coordinated platform-upgrade release has been started for this package yet."),
        };

        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(stub);
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateReleasesPage>(parameters =>
            parameters.Add(p => p.SelectedReleaseId, "11111111-2222-3333-4444-555555555555"));

        page.WaitForAssertion(
            () =>
            {
                // The coordinated section renders the established missing-state surface, not mock data.
                Assert.NotNull(page.Find("#coordinated"));
                Assert.Contains("No coordinated platform-upgrade release has been started", page.Markup, StringComparison.Ordinal);
                Assert.Empty(page.FindAll("#coordinated .operate-coordinated-timeline li"));
            },
            TimeSpan.FromSeconds(5));
    }

    private static GitOpsCoordinatedRelease BuildCoordinated() =>
        new(
            OperationId: "coordinated-release-pkg-democ",
            PackageId: "11111111-2222-3333-4444-555555555555",
            Status: "AwaitingApproval",
            CurrentStep: "MetadataAndSchema",
            TargetEnvironment: "production",
            CurrentPhase: "Awaiting explicit approval to apply the DB schema change.",
            Artifacts:
            [
                new GitOpsCoordinatedArtifact(GitOpsCoordinatedArtifactKind.ContainerImage, "Container rollout · prod-ecs", "honua/server:v21", "honua/server:v20"),
                new GitOpsCoordinatedArtifact(GitOpsCoordinatedArtifactKind.DatabaseSchema, "DB schema · add-owner-email", "Add nullable field 'owner_email' to parcels", null),
                new GitOpsCoordinatedArtifact(GitOpsCoordinatedArtifactKind.Metadata, "Metadata · pkg", "Activate new metadata revision in production", null),
            ],
            Steps:
            [
                new GitOpsCoordinatedStep("Preflight", GitOpsCoordinatedStepStatus.Succeeded, false, false, "Preflight passed."),
                new GitOpsCoordinatedStep("Backup", GitOpsCoordinatedStepStatus.Succeeded, false, false, "No-op for additive path."),
                new GitOpsCoordinatedStep("ContainerRollout", GitOpsCoordinatedStepStatus.Succeeded, true, true, "Container rollout promoted."),
                new GitOpsCoordinatedStep("MetadataAndSchema", GitOpsCoordinatedStepStatus.AwaitingApproval, true, true, "Awaiting approval to apply the DB schema change."),
                new GitOpsCoordinatedStep("Smoke", GitOpsCoordinatedStepStatus.Pending, false, false, null),
                new GitOpsCoordinatedStep("Promote", GitOpsCoordinatedStepStatus.Pending, false, false, null),
            ],
            ContainerGateApproved: true,
            DataGateApproved: false,
            RollbackReady: true,
            Blockers: [],
            Warnings: [],
            ErrorMessage: null);

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

        var dataScripts = new[]
        {
            new GitOpsDataScript("001", "001_add_parcels_index.sql", GitOpsDataScriptCoverage.Covered),
            new GitOpsDataScript("002", "002_drop_zoning_code.sql", GitOpsDataScriptCoverage.NoRollback),
        };

        return new GitOpsReleaseDetail(
            proposal,
            matrix,
            operation,
            OperateSectionStatus.Allowed,
            string.Empty,
            dataScripts);
    }

    private sealed class StubReleaseClient : IConsoleGitOpsReleaseClient
    {
        public OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>> Proposals { get; init; } =
            OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unsupported,
                "No release-package list endpoint.");

        public Func<string, OperateSectionResult<GitOpsReleaseDetail>> Detail { get; init; } =
            _ => OperateSectionResult<GitOpsReleaseDetail>.Denied(OperateSectionStatus.Missing, "Release not found.");

        public Func<string, OperateSectionResult<GitOpsCoordinatedRelease>> Coordinated { get; init; } =
            _ => OperateSectionResult<GitOpsCoordinatedRelease>.Denied(OperateSectionStatus.Missing, "No coordinated release.");

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

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Coordinated(releasePackageId));
    }
}
