using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateCopilotPageTests
{
    [Fact]
    public async Task RendersFindingsWithSeverityExplanationAndAction()
    {
        var html = await RenderAsync(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList())
        });

        Assert.Contains("Copilot Findings", html);
        Assert.Contains("Platform planes are skewed", html);
        Assert.Contains("not co-versioned", html);              // explanation.
        Assert.Contains("platform-release-skew", html);         // rule id.
        Assert.Contains("Propose fix", html);                   // action button.
        Assert.Contains("Geoprocessing queue is idle", html);
    }

    [Fact]
    public async Task RendersEmptyStateWhenNoFindings()
    {
        var html = await RenderAsync(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(new OpsFindingsListResponse
            {
                GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
                Findings = []
            })
        });

        Assert.Contains("No findings", html);
        Assert.Contains("all monitored conditions are healthy.", html);
    }

    [Fact]
    public async Task RendersMissingBindingWhenUnbound()
    {
        var html = await RenderAsync(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected. Connect an environment to load ops findings.")
        });

        Assert.Contains("Temporarily unavailable", html);
        Assert.Contains("Connect an environment", html);
    }

    [Fact]
    public void ProposeCreatedShowsProposalIdAndInboxLink()
    {
        using var ctx = new BunitContext();
        var client = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Allowed(new OpsFindingProposeResponse
            {
                FindingId = "platform-release-skew-abc123",
                Status = "ProposalCreated",
                ProposalId = "prop-42"
            })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(client);

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Equal("platform-release-skew-abc123", Assert.Single(client.ProposeCalls));
        Assert.Contains("prop-42", page.Markup);
        Assert.Contains("/inbox", page.Markup);
        Assert.Contains("Proposed", page.Markup);   // button flips to the proposed state.
    }

    [Fact]
    public void ProposeClearedConditionRefreshesListWithNotice()
    {
        using var ctx = new BunitContext();
        var client = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Denied(
                OperateSectionStatus.Missing,
                "This finding's condition has cleared or it no longer has a recommended action. The list has been refreshed.")
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(client);

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Equal(2, client.ListCalls);          // initial load + refresh after the 404.
        Assert.Contains("condition has cleared", page.Markup);
    }

    private static async Task<string> RenderAsync(IConsoleOpsFindingsClient client)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(client);
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<OperateCopilotPage>(ParameterView.Empty);
            return output.ToHtmlString();
        });
    }

    private static OpsFindingsListResponse BuildList() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-06-06T10:00:00Z"),
        Findings =
        [
            new OpsFindingResponse
            {
                Id = "platform-release-skew-abc123",
                Rule = "platform-release-skew",
                Severity = "Warning",
                Title = "Platform planes are skewed from the declared release",
                Explanation = "The worker plane is not co-versioned with release 2026.06.1.",
                DetectedAt = DateTimeOffset.Parse("2026-06-06T09:59:00Z"),
                Subject = new OpsFindingSubjectResponse { ReleaseVersion = "2026.06.1" },
                EvidenceRefs = ["release:2026.06.1"],
                RecommendedAction = new OpsFindingActionResponse
                {
                    Kind = "Deploy",
                    Summary = "Roll the worker plane to the declared release.",
                    Reason = "Restore co-versioning before the next coordinated deploy."
                }
            },
            new OpsFindingResponse
            {
                Id = "gp-queue-idle-def456",
                Rule = "gp-queue-idle",
                Severity = "Info",
                Title = "Geoprocessing queue is idle",
                Explanation = "No active geoprocessing jobs in the current window.",
                DetectedAt = DateTimeOffset.Parse("2026-06-06T09:58:00Z"),
                Subject = new OpsFindingSubjectResponse { WorkloadId = "local" },
                EvidenceRefs = []
            }
        ]
    };

    private sealed class StubOpsFindingsClient : IConsoleOpsFindingsClient
    {
        public OperateSectionResult<OpsFindingsListResponse> List { get; init; } =
            OperateSectionResult<OpsFindingsListResponse>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public OperateSectionResult<OpsFindingProposeResponse> ProposeResult { get; init; } =
            OperateSectionResult<OpsFindingProposeResponse>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public List<string> ProposeCalls { get; } = [];

        public int ListCalls { get; private set; }

        public Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(List);
        }

        public Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(string findingId, CancellationToken cancellationToken = default)
        {
            ProposeCalls.Add(findingId);
            return Task.FromResult(ProposeResult);
        }
    }
}
