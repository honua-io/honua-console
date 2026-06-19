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
    private static Bunit.TestContext NewContext(
        IConsoleDeployApprovalClient approvalClient,
        IConsoleServerVersionClient? versionClient = null,
        IConsoleGitOpsReleaseClient? releaseClient = null)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(approvalClient);
        ctx.Services.AddSingleton(versionClient
            ?? new InMemoryConsoleServerVersionClient(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));
        ctx.Services.AddSingleton(releaseClient ?? new EmptyReleaseClient());
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
    }
}
