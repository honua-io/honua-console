using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed partial class OperateCopilotPageTests
{
    [Theory]
    [InlineData(false, "Copilot Findings")]
    [InlineData(true, "Copilot findings")]
    public void RolloutKeepsFindingsAndAutonomyControls(bool v4Enabled, string heading)
    {
        var findings = new StubOpsFindingsClient { List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings()) };
        using var ctx = V4Context(v4Enabled, findings, new StubOpsAutonomyClient { LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(BuildAutonomySnapshot()) });

        var page = ctx.Render<OperateCopilotPage>();

        Assert.Equal(heading, Assert.Single(page.FindAll("h1")).TextContent);
        Assert.Equal(v4Enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(v4Enabled ? 1 : 0, page.FindAll("section.console-copilot-v4 > link[rel='stylesheet'][href$='operate-copilot-v4.css']").Count);
        Assert.Equal(v4Enabled ? 1 : 0, page.FindAll(".console-page-heading-actions [data-refresh-findings]").Count);
        Assert.Equal(v4Enabled ? 0 : 1, page.FindAll(".console-kicker").Count(kicker => kicker.TextContent == "Graduated operations"));
        Assert.Single(page.FindAll("[data-refresh-findings]"));
        Assert.NotNull(page.Find("#autonomy-policy-heading"));
        Assert.NotNull(page.Find("#autonomy-audit-heading"));
        Assert.NotNull(page.Find("[data-autonomy-kill-switch]"));
        Assert.NotEmpty(page.FindAll("[data-finding-id]"));
        if (v4Enabled)
        {
            V4PagePreview.Capture("copilot-findings", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshAndProposeStillCallTheServer(bool v4Enabled)
    {
        var findings = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Allowed(new OpsFindingProposeResponse
            {
                FindingId = "platform-release-skew-abc123",
                Status = "ProposalCreated",
                ProposalId = "prop-42",
            }),
        };
        using var ctx = V4Context(v4Enabled, findings, UnsupportedAutonomy());

        var page = ctx.Render<OperateCopilotPage>();
        page.Find("[data-refresh-findings]").Click();
        page.Find("[data-propose-finding='platform-release-skew-abc123']").Click();

        Assert.Equal(2, findings.ListCalls);
        Assert.Equal("platform-release-skew-abc123", Assert.Single(findings.ProposeCalls));
        Assert.Contains("inbox?proposalId=prop-42", page.Markup, StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-investigate-finding='gp-queue-idle-def456']"));
        Assert.Empty(page.FindAll("[data-autonomy-controls]"));
        if (v4Enabled)
        {
            V4PagePreview.Capture("copilot-proposed", page.Markup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnavailableFindingsKeepSharedStatusSurface(bool v4Enabled)
    {
        var findings = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "Connect an environment to load ops findings."),
        };
        using var ctx = V4Context(v4Enabled, findings, UnsupportedAutonomy());

        var page = ctx.Render<OperateCopilotPage>();

        Assert.Contains("Temporarily unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Connect an environment to load ops findings.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-finding-id]"));
        Assert.Single(page.FindAll("[data-refresh-findings]"));
    }

    private static BunitContext V4Context(bool v4Enabled, StubOpsFindingsClient findings, StubOpsAutonomyClient autonomy)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = v4Enabled });
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findings);
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());
        return ctx;
    }
}
