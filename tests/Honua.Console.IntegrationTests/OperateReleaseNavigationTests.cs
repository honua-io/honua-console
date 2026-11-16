using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class OperateReleasesPageRenderTests
{
    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "proposals")]
    [InlineData(ConsolePresentationMode.V4, "proposals")]
    [InlineData(ConsolePresentationMode.Legacy, "details")]
    [InlineData(ConsolePresentationMode.V4, "details")]
    [InlineData(ConsolePresentationMode.Legacy, "approvals")]
    [InlineData(ConsolePresentationMode.V4, "approvals")]
    public async Task EarlierListReadCannotReplaceReturnedReleaseList(ConsolePresentationMode mode, string stage)
    {
        var pending = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var reads = 0;
        var client = new StubReleaseClient
        {
            AsyncProposals = async () =>
            {
                var id = ++reads == 1 ? "earlier" : "current";
                if (stage == "proposals" && id == "earlier")
                {
                    started.SetResult();
                    await pending.Task;
                }
                return ReleaseProposals(id);
            },
            AsyncDetail = async id =>
            {
                if (stage == "details" && id == "earlier")
                {
                    started.SetResult();
                    await pending.Task;
                }
                return ReleaseDetail(id);
            }
        };
        var approvals = new ReleaseApprovalClient
        {
            Read = async ids =>
            {
                if (stage == "approvals" && ids.Contains("deploy-earlier"))
                {
                    started.SetResult();
                    await pending.Task;
                }
                return ApprovalStatus(ids.Single());
            }
        };
        using var context = ReleaseNavigationContext(mode, client, approvals);
        var page = context.Render<OperateReleasesPage>();
        page.WaitForAssertion(() => Assert.True(started.Task.IsCompleted));
        page.Render(p => p.Add(x => x.SelectedReleaseId, "detail"));
        page.Render(p => p.Add(x => x.SelectedReleaseId, (string?)null));
        page.WaitForAssertion(() => Assert.Contains("deploy-current", page.Markup));

        await page.InvokeAsync(() => pending.SetResult());

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Release current", page.Markup);
            Assert.Contains("deploy-current", page.Markup);
            Assert.DoesNotContain("Release earlier", page.Markup);
            Assert.DoesNotContain("deploy-earlier", page.Markup);
        });
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public void ReturningFromReleaseDetailRefreshesApprovalState(ConsolePresentationMode mode)
    {
        var reads = 0;
        var client = new StubReleaseClient
        {
            AsyncProposals = () => Task.FromResult(ReleaseProposals(++reads == 1 ? "before" : "after")),
            Detail = id => ReleaseDetail(id)
        };
        using var context = ReleaseNavigationContext(mode, client, new ReleaseApprovalClient());
        var page = context.Render<OperateReleasesPage>();
        page.WaitForAssertion(() => Assert.Contains("deploy-before", page.Markup));
        page.Render(p => p.Add(x => x.SelectedReleaseId, "before"));

        page.Render(p => p.Add(x => x.SelectedReleaseId, (string?)null));

        page.WaitForAssertion(() => Assert.Contains("deploy-after", page.Markup));
        Assert.DoesNotContain("deploy-before", page.Markup);
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy)]
    [InlineData(ConsolePresentationMode.V4)]
    public async Task ReturningListClearsPreviousEvidenceWhileRefreshing(ConsolePresentationMode mode)
    {
        var pending = new TaskCompletionSource<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>>();
        var reads = 0;
        var client = new StubReleaseClient
        {
            AsyncProposals = () => ++reads == 1 ? Task.FromResult(ReleaseProposals("before")) : pending.Task,
            Detail = id => ReleaseDetail(id)
        };
        using var context = ReleaseNavigationContext(mode, client, new ReleaseApprovalClient());
        var page = context.Render<OperateReleasesPage>();
        page.WaitForAssertion(() => Assert.Contains("deploy-before", page.Markup));
        page.Render(p => p.Add(x => x.SelectedReleaseId, "before"));

        page.Render(p => p.Add(x => x.SelectedReleaseId, (string?)null));

        Assert.DoesNotContain("Release before", page.Markup);
        Assert.DoesNotContain("deploy-before", page.Markup);
        await page.InvokeAsync(() => pending.SetResult(ReleaseProposals("after")));
        page.WaitForAssertion(() => Assert.Contains("deploy-after", page.Markup));
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "details")]
    [InlineData(ConsolePresentationMode.V4, "details")]
    [InlineData(ConsolePresentationMode.Legacy, "coordinated")]
    [InlineData(ConsolePresentationMode.V4, "coordinated")]
    public async Task EarlierReleaseDetailCannotReplaceCurrentRelease(ConsolePresentationMode mode, string stage)
    {
        var pending = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var client = new StubReleaseClient
        {
            AsyncDetail = async id =>
            {
                if (id == "earlier" && stage == "details")
                {
                    started.SetResult();
                    await pending.Task;
                }
                return ReleaseDetail(id);
            },
            AsyncCoordinated = async id =>
            {
                if (id == "earlier" && stage == "coordinated")
                {
                    started.SetResult();
                    await pending.Task;
                }
                return OperateSectionResult<GitOpsCoordinatedRelease>.Allowed(BuildCoordinated());
            }
        };
        using var context = ReleaseNavigationContext(mode, client, new ReleaseApprovalClient());
        var page = context.Render<OperateReleasesPage>(p => p.Add(x => x.SelectedReleaseId, "earlier"));
        page.WaitForAssertion(() => Assert.True(started.Task.IsCompleted));
        page.Render(p => p.Add(x => x.SelectedReleaseId, "current"));
        page.WaitForAssertion(() => Assert.Contains("Release current", page.Markup));

        await page.InvokeAsync(() => pending.SetResult());

        page.WaitForAssertion(() => Assert.Contains("Release current", page.Markup));
        Assert.DoesNotContain("Release earlier", page.Markup);
    }

    [Theory]
    [InlineData(ConsolePresentationMode.Legacy, "proposals")]
    [InlineData(ConsolePresentationMode.V4, "proposals")]
    [InlineData(ConsolePresentationMode.Legacy, "details")]
    [InlineData(ConsolePresentationMode.V4, "details")]
    public async Task DisposedListReadCannotStartFurtherServerReads(ConsolePresentationMode mode, string stage)
    {
        var pending = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var detailReads = 0;
        var approvalReads = 0;
        var client = new StubReleaseClient
        {
            AsyncProposals = async () =>
            {
                if (stage == "proposals")
                {
                    started.SetResult();
                    await pending.Task;
                }
                return ReleaseProposals("disposed");
            },
            AsyncDetail = async id =>
            {
                detailReads++;
                started.SetResult();
                await pending.Task;
                return ReleaseDetail(id);
            }
        };
        var approvals = new ReleaseApprovalClient
        {
            Read = ids =>
            {
                approvalReads++;
                return Task.FromResult(ApprovalStatus(ids.Single()));
            }
        };
        using var context = ReleaseNavigationContext(mode, client, approvals);
        var page = context.Render<OperateReleasesPage>();
        page.WaitForAssertion(() => Assert.True(started.Task.IsCompleted));

        await page.InvokeAsync(() =>
        {
            page.Instance.Dispose();
            pending.SetResult();
        });

        Assert.Equal(stage == "details" ? 1 : 0, detailReads);
        Assert.Equal(0, approvalReads);
    }

    private static BunitContext ReleaseNavigationContext(ConsolePresentationMode mode,
        StubReleaseClient releases, ReleaseApprovalClient approvals)
    {
        var context = new BunitContext();
        ConfigurePresentation(context, mode == ConsolePresentationMode.V4);
        context.Services.AddSingleton<IConsoleGitOpsReleaseClient>(releases);
        context.Services.AddSingleton<IConsoleDeployApprovalClient>(approvals);
        context.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        return context;
    }

    private static OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>> ReleaseProposals(string id) =>
        OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Allowed([ReleaseDetail(id).Value!.Proposal]);

    private static OperateSectionResult<GitOpsReleaseDetail> ReleaseDetail(string id)
    {
        var detail = BuildDetail(blocked: false);
        return OperateSectionResult<GitOpsReleaseDetail>.Allowed(detail with
        {
            Proposal = detail.Proposal with { ReleasePackageId = id, Title = $"Release {id}" },
            Operation = detail.Operation! with { DeployOperationId = $"deploy-{id}" }
        });
    }

    private static OperateSectionResult<IReadOnlyList<DeployOperationProposal>> ApprovalStatus(string id) =>
        OperateSectionResult<IReadOnlyList<DeployOperationProposal>>.Denied(OperateSectionStatus.Unavailable, id);

    private sealed class ReleaseApprovalClient : IConsoleDeployApprovalClient
    {
        private readonly UnsupportedConsoleDeployApprovalClient _unsupported = new();
        public Func<IReadOnlyList<string>, Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>>> Read { get; init; } =
            ids => Task.FromResult(ApprovalStatus(ids.Single()));

        public Task<OperateSectionResult<IReadOnlyList<DeployOperationProposal>>> ListPendingAsync(
            IReadOnlyList<string> operationIds, CancellationToken cancellationToken = default) => Read(operationIds);

        public Task<OperateSectionResult<DeployOperationProposal>> GetOperationAsync(
            string operationId, CancellationToken cancellationToken = default) => _unsupported.GetOperationAsync(operationId, cancellationToken);

        public Task<OperateSectionResult<ConsoleDeployApprovalReview>> OpenSubmitReviewAsync(
            string operationId, CancellationToken cancellationToken = default) => _unsupported.OpenSubmitReviewAsync(operationId, cancellationToken);

        public Task<OperateSectionResult<DeployOperationProposal>> SubmitAsync(
            ConsoleDeployApprovalReview review, string? reason, CancellationToken cancellationToken = default) => _unsupported.SubmitAsync(review, reason, cancellationToken);

        public Task<OperateSectionResult<DeployOperationProposal>> RollbackAsync(
            string operationId, string? reason, CancellationToken cancellationToken = default) => _unsupported.RollbackAsync(operationId, reason, cancellationToken);
    }
}
