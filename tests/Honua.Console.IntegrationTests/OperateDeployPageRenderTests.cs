using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the unified Operate → Deploy page. The page hosts
/// both governed capabilities behind one flow: server-version upgrade and cross-environment
/// metadata promotion. The deploy-control queues bind to a live server (charter section 11);
/// these tests drive the page through the test/demo in-memory clients to assert:
///   - both capability sections render with the missing/empty surfaces honestly,
///   - a tracked server-upgrade operation binds the governed outcome panel (with rollback), and
///   - a tracked metadata-promotion operation routes into the promotion list, not the upgrade card.
/// </summary>
public sealed class OperateDeployPageRenderTests
{
    private static BunitContext NewContext(
        IConsoleDeployApprovalClient approvalClient,
        IConsoleServerVersionClient? versionClient = null,
        IConsoleGitOpsReleaseClient? releaseClient = null,
        IConsoleDeployOperationsClient? deployOperationsClient = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(approvalClient);
        ctx.Services.AddSingleton(versionClient
            ?? new InMemoryConsoleServerVersionClient(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));
        ctx.Services.AddSingleton(releaseClient ?? new EmptyReleaseClient());
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        // console#290: "All deploy operations" and the platform-release converge card both
        // require IConsoleDeployOperationsClient. The realtime client is resolved OPTIONALLY
        // (GetService) by OperateDeployOperationsList, so it is intentionally left unregistered
        // here — the list renders its honest Manual pill, exactly like a host that has not
        // wired up honua-server#2554 yet.
        ctx.Services.AddSingleton(deployOperationsClient ?? new InMemoryConsoleDeployOperationsClient());
        return ctx;
    }

    [Fact]
    public void RendersBothCapabilitySections()
    {
        using var ctx = NewContext(new InMemoryConsoleDeployApprovalClient());

        var page = ctx.Render<OperateDeployPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.NotNull(page.Find("#server-upgrade"));
                Assert.NotNull(page.Find("#cross-env-promote"));
                Assert.NotNull(page.Find("#deploy-approvals"));
                // The cross-env section links to the full release surface.
                Assert.Equal("/operate/releases", page.Find("[data-open-releases]").GetAttribute("href"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NoReleaseProposals_RendersHonestEmptyPromoteState()
    {
        using var ctx = NewContext(new InMemoryConsoleDeployApprovalClient());

        var page = ctx.Render<OperateDeployPage>();

        page.WaitForAssertion(
            () =>
            {
                // The "available to promote" surface renders the honest empty state, not a
                // fabricated list, when the server reports no release proposals.
                Assert.Empty(page.FindAll("[data-promote-proposal]"));
                Assert.Contains("No promotions available", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AvailablePromotions_RenderSourceToTargets_AndLinkToReleaseDetail()
    {
        using var ctx = NewContext(
            new InMemoryConsoleDeployApprovalClient(),
            releaseClient: new StubReleaseClient(
            [
                new GitOpsReleaseProposal(
                    ReleasePackageId: "rel-pkg-7",
                    Title: "Promote staging metadata",
                    Summary: "Promote 3 semantic resources from staging.",
                    SourceEnvironmentId: "staging",
                    TargetEnvironmentIds: ["prod"],
                    DesiredRevision: "rev-12",
                    ChangedResources:
                    [
                        new GitOpsChangedResource("svc/a", "service", "Service A", [GitOpsChangeClass.Metadata]),
                    ],
                    RollbackClassification: GitOpsRollbackClassification.MetadataOnly,
                    HasBlockingFindings: false),
            ]));

        var page = ctx.Render<OperateDeployPage>();

        page.WaitForAssertion(
            () =>
            {
                var row = page.Find("[data-promote-proposal='rel-pkg-7']");
                // The proposal links into the governed release detail (where promotion is approved).
                var link = row.QuerySelector("a.operate-deploy-promote-link")!;
                Assert.Equal("/operate/releases/rel-pkg-7", link.GetAttribute("href"));
                // Source → targets and the change count are visible directly on the Deploy page.
                Assert.Contains("staging", row.InnerHtml, StringComparison.Ordinal);
                Assert.Contains("prod", row.InnerHtml, StringComparison.Ordinal);
                Assert.Contains("1 semantic change", row.InnerHtml, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TrackingServerUpgradeOperation_BindsGovernedOutcome_AndRollsBack()
    {
        var approvalClient = new InMemoryConsoleDeployApprovalClient([SubmittedUpgrade("upgrade-op")]);
        using var ctx = NewContext(
            approvalClient,
            versionClient: new InMemoryConsoleServerVersionClient(new ServerVersionInfo(
                "1.4.2", "v1", "v2", "2", "staging", DateTimeOffset.UtcNow)));

        var page = ctx.Render<OperateDeployPage>();

        // Track the upgrade operation id (the server has no list-all endpoint).
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-track-operation-id]")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-track-operation-id]").Change("upgrade-op");
        page.Find("[data-track-operation]").Click();

        // The upgrade card now shows the governed outcome panel; rollback requires explicit confirm.
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-upgrade-outcome] button.operate-deploy-rollback-button")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-upgrade-outcome] button.operate-deploy-rollback-button").Click();
        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-upgrade-outcome] .operate-deploy-confirm")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-upgrade-outcome] button.console-button-danger").Click();
        page.WaitForAssertion(
            () => Assert.Contains("rollback requested", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TrackingPromotionOperation_RoutesIntoPromotionList_NotUpgradeCard()
    {
        var approvalClient = new InMemoryConsoleDeployApprovalClient([AwaitingPromotion("promo-op")]);
        using var ctx = NewContext(approvalClient);

        var page = ctx.Render<OperateDeployPage>();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-track-operation-id]")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-track-operation-id]").Change("promo-op");
        page.Find("[data-track-operation]").Click();

        page.WaitForAssertion(
            () =>
            {
                // The promotion appears in the cross-env promotion list.
                var promoSection = page.Find("#cross-env-promote");
                Assert.Contains("promo-op", promoSection.InnerHtml, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    private static DeployOperationProposal SubmittedUpgrade(string operationId) => new(
        OperationId: operationId,
        Lifecycle: DeployOperationLifecycle.Submitted,
        RawStatus: "Submitted",
        Kind: "Deploy",
        Priority: "normal",
        Service: "honua-server",
        Environment: "staging",
        DesiredRevision: "1.5.0",
        CurrentRevision: "1.4.2",
        Action: "deploy",
        ChangeSummary: "Upgrade honua-server to 1.5.0.",
        RequestedBy: "honua-devops",
        Reason: "server upgrade",
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: new DeployOperationRollbackSummary(
            GitOpsRollbackClassification.SnapshotRequired,
            IsDataAffecting: true,
            RequiresExplicitApproval: true,
            Steps: ["restore prior image"],
            EvidenceRequired: ["image-digest"],
            ApprovalPolicyRef: "policy/rollback-server"),
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
        UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-1));

    private static DeployOperationProposal AwaitingPromotion(string operationId) => SubmittedUpgrade(operationId) with
    {
        Lifecycle = DeployOperationLifecycle.AwaitingApproval,
        RawStatus = "AwaitingApproval",
        Kind = "MetadataRelease",
        Service = "MetadataRelease",
        Environment = "prod",
        RollbackPlan = null,
    };

    // Returns a fixed set of release proposals so the "available to promote" surface can be
    // asserted. Detail/coordinated reads are not exercised by these promote-list tests.
    private sealed class StubReleaseClient(IReadOnlyList<GitOpsReleaseProposal> proposals)
        : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(proposals));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Missing,
                "Release not found."));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Missing,
                "Release not found."));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Missing,
                "No coordinated release."));
    }

    private sealed class EmptyReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
                (IReadOnlyList<GitOpsReleaseProposal>)[]));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Missing,
                "Release not found."));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Missing,
                "Release not found."));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Missing,
                "No coordinated release."));
    }
}
