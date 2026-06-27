using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render regression for the repositioned console home (#193). The landing is
/// no longer a dead link grid: it leads with the approval-inbox summary (pending agent
/// work) and a recent-activity panel, then the area work surfaces. These tests assert the
/// inbox band binds the aggregated queue and deep-links into the full inbox, and that the
/// four area cards still render as entry points.
/// </summary>
public sealed class ConsoleHomePageRenderTests
{
    private static BunitContext NewContext(IConsoleDeployApprovalClient approvalClient)
    {
        var ctx = new BunitContext();
        var releaseClient = new ApprovalInboxPageRenderTests_EmptyReleaseClient();
        ctx.Services.AddSingleton<IConsoleEnvironmentProfileStore>(
            new InMemoryConsoleEnvironmentProfileStore([]));
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(releaseClient);
        ctx.Services.AddSingleton(approvalClient);
        ctx.Services.AddSingleton<IConsoleApprovalInboxClient>(
            new ConsoleApprovalInboxClient(releaseClient, approvalClient));
        ctx.Services.AddSingleton<IConsoleHostCapabilities>(
            new BrowserConsoleHostCapabilities());
        return ctx;
    }

    [Fact]
    public void Home_LeadsWithInboxSummary_AndDeepLinksToInbox()
    {
        using var ctx = NewContext(new InMemoryConsoleDeployApprovalClient());

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                Assert.Equal("0", page.Find("[data-home-awaiting-count] strong").TextContent.Trim());
                Assert.Equal("/inbox", page.Find("[data-open-inbox]").GetAttribute("href"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Home_StillRendersTheFourAreaWorkSurfaces()
    {
        using var ctx = NewContext(new InMemoryConsoleDeployApprovalClient());

        var page = ctx.Render<ConsoleHomePage>();

        page.WaitForAssertion(
            () =>
            {
                foreach (var area in ConsoleRouteMap.Areas)
                {
                    Assert.Contains(area.Name, page.Markup, StringComparison.Ordinal);
                }
            },
            TimeSpan.FromSeconds(5));
    }

    // The empty release client lives on the inbox render-test type; re-declared here as an
    // alias-free local to keep this file self-contained without a shared test helper.
    private sealed class ApprovalInboxPageRenderTests_EmptyReleaseClient : IConsoleGitOpsReleaseClient
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
