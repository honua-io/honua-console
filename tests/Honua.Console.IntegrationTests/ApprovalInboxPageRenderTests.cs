using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the approval inbox (#193) — the GIS-department work
/// queue. The inbox aggregates agent-proposed deploy-control operations into one
/// human-in-the-loop surface, classified by ticket type. These tests drive the page through
/// the test/demo in-memory deploy client (charter section 11: a live page binds to a server)
/// to assert:
///   - a denied deploy-control read renders the shared missing/unavailable surface,
///   - an empty queue renders the honest empty state,
///   - a populated queue renders summary counts, ticket-type filter chips, and queue rows, and
///   - selecting a row binds the governed approval panel for review.
/// </summary>
public sealed class ApprovalInboxPageRenderTests
{
    private static BunitContext NewContext(IConsoleDeployApprovalClient approvalClient)
    {
        var ctx = new BunitContext();
        var releaseClient = new EmptyReleaseClient();
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(releaseClient);
        ctx.Services.AddSingleton(approvalClient);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(releaseClient, approvalClient));
        return ctx;
    }

    [Fact]
    public void DeniedRead_RendersStatusSurface()
    {
        using var ctx = NewContext(
            new DeniedDeployApprovalClient(OperateSectionStatus.Unavailable, "No active environment profile is selected."));

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () => Assert.Contains("No active environment profile is selected.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmptyQueue_RendersHonestEmptyState_AndZeroCounts()
    {
        using var ctx = NewContext(new InMemoryConsoleDeployApprovalClient());

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("0", page.Find("[data-awaiting-count] strong").TextContent.Trim());
                Assert.Contains("No work in the queue", page.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TrackedOperation_RendersClassifiedRow_AndSelectingItOpensApprovalPanel()
    {
        var approvalClient = new InMemoryConsoleDeployApprovalClient([Awaiting("promo-op", "MetadataRelease")]);
        using var ctx = NewContext(approvalClient);

        var page = ctx.Render<ApprovalInboxPage>();

        page.WaitForAssertion(
            () => Assert.NotNull(page.Find("[data-track-operation-id]")),
            TimeSpan.FromSeconds(5));

        page.Find("[data-track-operation-id]").Change("promo-op");
        page.Find("[data-track-operation]").Click();

        // The proposal appears as a classified queue row (Publish / update data ticket type).
        page.WaitForAssertion(
            () =>
            {
                var row = page.Find("[data-operation-id=\"promo-op\"]");
                Assert.Contains("Publish / update data", row.InnerHtml, StringComparison.Ordinal);
                Assert.Equal("1", page.Find("[data-awaiting-count] strong").TextContent.Trim());
            },
            TimeSpan.FromSeconds(5));

        // Selecting the row binds the governed approval panel (review-before-authorize).
        page.Find("[data-operation-id=\"promo-op\"]").Click();
        page.WaitForAssertion(
            () => Assert.Contains("Deploy approval", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    private static DeployOperationProposal Awaiting(string operationId, string kind) => new(
        OperationId: operationId,
        Lifecycle: DeployOperationLifecycle.AwaitingApproval,
        RawStatus: "AwaitingApproval",
        Kind: kind,
        Priority: "normal",
        Service: kind,
        Environment: "prod",
        DesiredRevision: "rev-1",
        CurrentRevision: null,
        Action: "promote",
        ChangeSummary: "summary",
        RequestedBy: "agent",
        Reason: null,
        PrUrl: null,
        CommitSha: null,
        Evidence: [],
        RollbackPlan: null,
        Warnings: [],
        BlockingReasons: [],
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    private sealed class DeniedDeployApprovalClient : IConsoleDeployApprovalClient
    {
        private readonly OperateSectionStatus _status;
        private readonly string _message;

        public DeniedDeployApprovalClient(OperateSectionStatus status, string message)
        {
            _status = status;
            _message = message;
        }

        public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
            string operationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));

        public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
            IReadOnlyList<string> operationIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(_status, _message));

        public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
            string operationId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));

        public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
            string operationId, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<DeployOperationProposal>.Denied(_status, _message));
    }

    private sealed class EmptyReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed(
                (IReadOnlyList<GitOpsReleaseProposal>)[]));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Missing, "Release not found."));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Missing, "Release not found."));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Missing, "No coordinated release."));
    }
}
