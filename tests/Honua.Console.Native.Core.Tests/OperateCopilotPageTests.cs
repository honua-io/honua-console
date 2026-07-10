using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
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

        // console#292: every finding is anchored so a correlation-id chip's FindingDetail route
        // (/operate/copilot#finding-{id}) resolves to a real element, never a dead anchor.
        Assert.Contains("id=\"finding-platform-release-skew-abc123\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"finding-gp-queue-idle-def456\"", html, StringComparison.Ordinal);

        // console#292: a subject's pinned deploy operation renders as a correlation-id chip
        // deep-linking into the Deploy page rather than plain text.
        Assert.Contains("data-correlation-kind=\"OperationId\"", html, StringComparison.Ordinal);
        Assert.Contains("/operate/deploy?operationId=deploy-op-42#deploy-approvals", html, StringComparison.Ordinal);

        // console#292 scope item 6: the informational finding (no RecommendedAction) still gets
        // a next step — "Investigate" linking to the correlated evidence timeline since it has no
        // pinned operation id.
        Assert.Contains("data-investigate-finding=\"gp-queue-idle-def456\"", html, StringComparison.Ordinal);
        Assert.Contains("/operate/observability#events", html, StringComparison.Ordinal);
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
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(UnsupportedAutonomy());
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Equal("platform-release-skew-abc123", Assert.Single(client.ProposeCalls));
        Assert.Contains("prop-42", page.Markup);
        Assert.Contains("/inbox", page.Markup);
        Assert.Contains("Proposed", page.Markup);   // button flips to the proposed state.

        // console#292: the created proposal id renders as a correlation-id chip deep-linking
        // into the approval inbox, preselected, not a bare <code> string.
        Assert.Contains("data-correlation-kind=\"ProposalId\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("/inbox?proposalId=prop-42", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ProposeBlockedOutcomeShowsErrorReasonNotProposed()
    {
        // The server returns HTTP 200 for a governed Blocked outcome (only cleared/no-action
        // map to 404). The page must NOT present it as a successful "Proposed" with a proposal
        // link, and the propose button must stay actionable so the operator can retry.
        using var ctx = new BunitContext();
        var client = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Allowed(new OpsFindingProposeResponse
            {
                FindingId = "platform-release-skew-abc123",
                Status = "Blocked",
                ProposalId = "prop-should-not-appear",
                Message = "The guardrail ladder denied this deploy for the current edition."
            })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(client);
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(UnsupportedAutonomy());
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Equal("platform-release-skew-abc123", Assert.Single(client.ProposeCalls));
        // Reason surfaces as an error-styled outcome.
        Assert.Contains("guardrail ladder denied", page.Markup);
        Assert.Contains("operate-status-denied", page.Markup);
        // No bogus success affordances.
        Assert.DoesNotContain("prop-should-not-appear", page.Markup);
        Assert.DoesNotContain("review in the approval inbox", page.Markup);
        // Button stays actionable (still "Propose fix", not a disabled "Proposed").
        Assert.Contains(
            page.FindAll("button"),
            b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal));
        Assert.DoesNotContain(
            page.FindAll("button"),
            b => b.TextContent.Contains("Proposed", StringComparison.Ordinal)
                && !b.TextContent.Contains("Propose fix", StringComparison.Ordinal));
    }

    [Fact]
    public void ProposeNotSupportedOutcomeShowsErrorNotProposed()
    {
        using var ctx = new BunitContext();
        var client = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Allowed(new OpsFindingProposeResponse
            {
                FindingId = "platform-release-skew-abc123",
                Status = "NotSupported",
                Message = "This operation class is not supported by the gateway."
            })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(client);
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(UnsupportedAutonomy());
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Contains("not supported by the gateway", page.Markup);
        Assert.Contains("operate-status-denied", page.Markup);
        Assert.DoesNotContain("review in the approval inbox", page.Markup);
        Assert.Contains(
            page.FindAll("button"),
            b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal));
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
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(UnsupportedAutonomy());
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Propose fix", StringComparison.Ordinal)).Click();

        Assert.Equal(2, client.ListCalls);          // initial load + refresh after the 404.
        Assert.Contains("condition has cleared", page.Markup);
    }

    [Fact]
    public void AutonomySurfaceRendersGuardrailsGraduationEvidenceAndCausalAudit()
    {
        using var ctx = new BunitContext();
        var findings = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        };
        var autonomy = new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(BuildAutonomySnapshot())
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findings);
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();

        Assert.Contains("Autonomy policy", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Server-confirmed", page.Markup, StringComparison.Ordinal);
        Assert.Contains("2 actions / 60 minutes", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Max blast radius", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Yes — blast radius 1", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Not automatable", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Effective config/default projection", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Proposals raised", page.Markup, StringComparison.Ordinal);
        Assert.Contains(">12<", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Auto-applied", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Rolled back", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-timeline-row", page.Markup, StringComparison.Ordinal);
        Assert.Contains("operation.auto_applied", page.Markup, StringComparison.Ordinal);
        Assert.Contains("ops_autonomy.policy.update", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-correlation-kind=\"CorrelationId\"", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OlderServerHidesAutonomyControlsAndKeepsProposeOnlyFlow()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildList())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(UnsupportedAutonomy());
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();

        Assert.DoesNotContain("data-autonomy-controls", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Propose fix", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Unsupported by this server", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void FreshPolicyStoreDoesNotGuessConfigBackedModeAndOffersSafeBaseline()
    {
        using var ctx = new BunitContext();
        var snapshot = BuildAutonomySnapshot() with { Policies = [] };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(snapshot)
        });
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();

        Assert.Contains("config-backed effective mode", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Pin propose-only", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-finding-autonomy=\"PolicyNotReported\"", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable auto-apply", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyWithoutActiveActionFailsClosedForGraduationToAutoApply()
    {
        using var ctx = new BunitContext();
        var snapshot = BuildAutonomySnapshot();
        var unknownActionPolicy = new OpsAutonomyPolicyResponse
        {
            Rule = "rule-without-active-finding",
            Mode = "ProposeOnly",
            MaxAutoActionsPerWindow = 1,
            WindowSeconds = 3600,
            MaxBlastRadius = 1,
            IsPersisted = true,
            TrackRecord = new OpsAutonomyTrackRecordResponse()
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(snapshot with
            {
                Policies = [.. snapshot.Policies, unknownActionPolicy]
            })
        });
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        var policy = page.Find("[data-policy-rule=\"rule-without-active-finding\"]");

        Assert.Contains("no active action", policy.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not automatable", policy.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("data-request-policy", policy.OuterHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void KillSwitchRequiresConfirmationAndRendersReturnedServerState()
    {
        using var ctx = new BunitContext();
        var autonomy = new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(BuildAutonomySnapshot()),
            SettingsResult = OperateSectionResult<OpsAutonomySettingsResponse>.Allowed(new OpsAutonomySettingsResponse
            {
                // The server is authoritative: deliberately return disabled even though the
                // requested value is true. The UI must not render the requested state.
                KillSwitchEnabled = false,
                UpdatedBy = "operator.server"
            })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());
        var page = ctx.Render<OperateCopilotPage>();

        page.Find("[data-request-kill-switch]").Click();

        Assert.Empty(autonomy.KillSwitchCalls);
        Assert.NotNull(page.Find("[data-confirm-kill-switch]"));
        Assert.Contains("Kill switch disabled", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Per-rule modes enforced", page.Markup, StringComparison.Ordinal);

        page.Find("[data-confirm-kill-switch]").Click();

        Assert.True(Assert.Single(autonomy.KillSwitchCalls));
        Assert.Contains("Kill switch disabled", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Kill switch enabled", page.Markup, StringComparison.Ordinal);
        Assert.Contains("server kept", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PolicyToggleRequiresConfirmationAndRendersReturnedServerMode()
    {
        using var ctx = new BunitContext();
        var snapshot = BuildAutonomySnapshot() with
        {
            AuditEntries = []
        };
        var policy = snapshot.Policies.Single(item => item.Rule == "alert-dispatch-backlog");
        var autonomy = new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(snapshot),
            PolicyResult = OperateSectionResult<OpsAutonomyPolicyResponse>.Allowed(
                policy with { Mode = "ProposeOnly", UpdatedBy = "operator.server" })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());
        var page = ctx.Render<OperateCopilotPage>();

        page.Find("[data-request-policy=\"alert-dispatch-backlog\"]").Click();

        Assert.Empty(autonomy.PolicyCalls);
        Assert.NotNull(page.Find("[data-confirm-policy=\"alert-dispatch-backlog\"]"));

        page.Find("[data-confirm-policy=\"alert-dispatch-backlog\"]").Click();

        var call = Assert.Single(autonomy.PolicyCalls);
        Assert.Equal(("alert-dispatch-backlog", "AutoApply"), (call.Rule, call.Mode));
        Assert.Contains("Propose only", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "ProposeOnly",
            page.Find("[data-policy-rule=\"alert-dispatch-backlog\"]").GetAttribute("data-policy-mode"));
        Assert.Contains("server kept", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuccessfulPolicyChangeImmediatelyRefreshesServerAuditEvidence()
    {
        using var ctx = new BunitContext();
        var initial = BuildAutonomySnapshot() with { AuditEntries = [] };
        var initialPolicy = initial.Policies.Single(item => item.Rule == "alert-dispatch-backlog");
        var confirmedPolicy = initialPolicy with { Mode = "AutoApply", IsPersisted = true };
        var refreshed = BuildAutonomySnapshot() with
        {
            Policies = initial.Policies
                .Where(item => item.Rule != confirmedPolicy.Rule)
                .Append(confirmedPolicy)
                .ToArray()
        };
        var autonomy = new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(initial),
            ReloadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(refreshed),
            PolicyResult = OperateSectionResult<OpsAutonomyPolicyResponse>.Allowed(confirmedPolicy)
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(autonomy);
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());
        var page = ctx.Render<OperateCopilotPage>();

        page.Find("[data-request-policy=\"alert-dispatch-backlog\"]").Click();
        page.Find("[data-confirm-policy=\"alert-dispatch-backlog\"]").Click();

        Assert.Equal(2, autonomy.LoadCalls);
        Assert.Equal(
            "AutoApply",
            page.Find("[data-policy-rule=\"alert-dispatch-backlog\"]").GetAttribute("data-policy-mode"));
        Assert.Contains("ops_autonomy.policy.update", page.Markup, StringComparison.Ordinal);
        Assert.Contains("policy-change audit event is visible", page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindingCardsDistinguishProposedAutoAppliedAndGuardrailBlocked()
    {
        using var ctx = new BunitContext();
        var findings = new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings()),
            ProposeResult = OperateSectionResult<OpsFindingProposeResponse>.Allowed(new OpsFindingProposeResponse
            {
                FindingId = "platform-release-skew-abc123",
                Status = "ProposalCreated",
                ProposalId = "proposal-7"
            })
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(findings);
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(BuildAutonomySnapshot())
        });
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());
        var page = ctx.Render<OperateCopilotPage>();

        Assert.Contains("data-finding-autonomy=\"AutoApplied\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("data-finding-autonomy=\"GuardrailBlocked\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Blocked by guardrail", page.Markup, StringComparison.Ordinal);

        page.Find("[data-propose-finding=\"platform-release-skew-abc123\"]").Click();

        Assert.Contains("data-finding-autonomy=\"AwaitingApproval\"", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Proposed — awaiting approval", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalAutoApplyDoesNotLabelARecurrentActiveFindingAsAlreadyApplied()
    {
        using var ctx = new BunitContext();
        var snapshot = BuildAutonomySnapshot();
        var stale = snapshot.AuditEntries[0] with
        {
            OccurredAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z")
        };
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(
                snapshot with { AuditEntries = [stale, snapshot.AuditEntries[1]] })
        });
        ctx.Services.AddSingleton<IConsoleProposalsClient>(NoProposals());

        var page = ctx.Render<OperateCopilotPage>();
        var finding = page.Find("[data-finding-id=\"alert-dispatch-backlog-auto\"]");

        Assert.DoesNotContain("data-finding-autonomy=\"AutoApplied\"", finding.OuterHtml, StringComparison.Ordinal);
        Assert.Contains("data-finding-autonomy=\"ProposeOnly\"", finding.OuterHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerLinkedPendingProposalSurvivesRefreshAsAwaitingApprovalState()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleOpsFindingsClient>(new StubOpsFindingsClient
        {
            List = OperateSectionResult<OpsFindingsListResponse>.Allowed(BuildAutonomyFindings())
        });
        ctx.Services.AddSingleton<IConsoleOpsAutonomyClient>(new StubOpsAutonomyClient
        {
            LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Allowed(BuildAutonomySnapshot())
        });
        ctx.Services.AddSingleton<IConsoleProposalsClient>(new StubProposalsClient
        {
            ListResult = OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
            [
                new ConsoleProposalSummary(
                    "proposal-persisted",
                    ConsoleProposalKind.Deploy,
                    ConsoleProposalStatus.AwaitingApproval,
                    "ops-agent",
                    null,
                    "Restore platform co-versioning",
                    ConsoleProposalRisk.Medium,
                    DateTimeOffset.Parse("2026-07-10T09:58:00Z"),
                    DateTimeOffset.Parse("2026-07-10T09:59:00Z"))
                {
                    FindingId = "platform-release-skew-abc123",
                    AutonomyRule = "platform-release-skew",
                    ActionDiscriminator = "release.converge"
                }
            ])
        });

        var page = ctx.Render<OperateCopilotPage>();
        var finding = page.Find("[data-finding-id=\"platform-release-skew-abc123\"]");

        Assert.Contains("data-finding-autonomy=\"AwaitingApproval\"", finding.OuterHtml, StringComparison.Ordinal);
        Assert.Contains("Proposed — awaiting approval", finding.TextContent, StringComparison.Ordinal);
        Assert.Contains("data-finding-pending-proposal=\"proposal-persisted\"", finding.OuterHtml, StringComparison.Ordinal);
        Assert.Contains("/inbox?proposalId=proposal-persisted", finding.OuterHtml, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync(
        IConsoleOpsFindingsClient client,
        IConsoleOpsAutonomyClient? autonomy = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(client);
        services.AddSingleton(autonomy ?? UnsupportedAutonomy());
        services.AddSingleton<IConsoleProposalsClient>(NoProposals());
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
                Subject = new OpsFindingSubjectResponse { ReleaseVersion = "2026.06.1", OperationId = "deploy-op-42" },
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

    private static OpsFindingsListResponse BuildAutonomyFindings() => new()
    {
        GeneratedAt = DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
        Findings =
        [
            .. BuildList().Findings!,
            new OpsFindingResponse
            {
                Id = "alert-dispatch-backlog-auto",
                Rule = "alert-dispatch-backlog",
                Severity = "Critical",
                Title = "Alert dispatch dead letters exceed the threshold",
                Explanation = "Seven notifications are dead-lettered.",
                DetectedAt = DateTimeOffset.Parse("2026-07-10T09:58:00Z"),
                Subject = new OpsFindingSubjectResponse { Channel = "webhook" },
                EvidenceRefs = ["dispatch:dead-letter:7"],
                RecommendedAction = new OpsFindingActionResponse
                {
                    Kind = "AdminConfigChange",
                    Summary = "Redrive bounded dead letters.",
                    Reason = "Restore alert delivery.",
                    AutoSafe = true,
                    BlastRadius = 1
                }
            },
            new OpsFindingResponse
            {
                Id = "cache-pressure-blocked",
                Rule = "cache-pressure",
                Severity = "Warning",
                Title = "Cache pressure is elevated",
                Explanation = "The bounded cleanup would affect five partitions.",
                DetectedAt = DateTimeOffset.Parse("2026-07-10T09:57:00Z"),
                Subject = new OpsFindingSubjectResponse { WorkloadId = "cache" },
                EvidenceRefs = ["metric:cache-pressure"],
                RecommendedAction = new OpsFindingActionResponse
                {
                    Kind = "AdminConfigChange",
                    Summary = "Run bounded cache cleanup.",
                    Reason = "Relieve cache pressure.",
                    AutoSafe = true,
                    BlastRadius = 5
                }
            }
        ]
    };

    private static OpsAutonomySnapshot BuildAutonomySnapshot() => new(
        Settings: new OpsAutonomySettingsResponse
        {
            KillSwitchEnabled = false,
            UpdatedAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
            UpdatedBy = "operator.alice"
        },
        Policies:
        [
            new OpsAutonomyPolicyResponse
            {
                Rule = "alert-dispatch-backlog",
                Mode = "ProposeOnly",
                MaxAutoActionsPerWindow = 2,
                WindowSeconds = 3600,
                MaxBlastRadius = 1,
                UpdatedAt = DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
                UpdatedBy = "operator.alice",
                IsPersisted = false,
                TrackRecord = new OpsAutonomyTrackRecordResponse
                {
                    ProposalsRaised = 12,
                    ProposalsApproved = 9,
                    ProposalsRejected = 3,
                    AutoApplied = 6,
                    RolledBack = 1,
                    Failed = 1,
                    FirstActivityAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
                    LastActivityAt = DateTimeOffset.Parse("2026-07-10T09:30:00Z")
                }
            },
            new OpsAutonomyPolicyResponse
            {
                Rule = "cache-pressure",
                Mode = "AutoApply",
                MaxAutoActionsPerWindow = 1,
                WindowSeconds = 3600,
                MaxBlastRadius = 1,
                IsPersisted = true,
                TrackRecord = new OpsAutonomyTrackRecordResponse()
            }
        ],
        AuditEntries:
        [
            new OpsAutonomyAuditEntry(
                "audit:91",
                DateTimeOffset.Parse("2026-07-10T10:00:00Z"),
                "operation.auto_applied",
                "ops-autonomy",
                "operation-42",
                "operation_autonomy/alert-dispatch-backlog-auto",
                "alert-dispatch-backlog-auto",
                "alert-dispatch-backlog",
                ["dispatch:dead-letter:7"],
                "Backlog remained clear.",
                new OpsAutonomyOutcome("Auto-applied", new OperateStatus("succeeded", "Verified and converged.")),
                false),
            new OpsAutonomyAuditEntry(
                "audit:90",
                DateTimeOffset.Parse("2026-07-10T09:00:00Z"),
                "ops_autonomy.policy.update",
                "operator.alice",
                "alert-dispatch-backlog",
                "ops_autonomy_policy/alert-dispatch-backlog",
                null,
                "alert-dispatch-backlog",
                [],
                "Policy changed to ProposeOnly.",
                new OpsAutonomyOutcome("Policy changed", new OperateStatus("info", "Audited policy mutation.")),
                true)
        ]);

    private static StubOpsAutonomyClient UnsupportedAutonomy() => new()
    {
        LoadResult = OperateSectionResult<OpsAutonomySnapshot>.Denied(
            OperateSectionStatus.Unsupported,
            "The connected server does not expose graduated ops autonomy.")
    };

    private static StubProposalsClient NoProposals() => new()
    {
        ListResult = OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([])
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

    private sealed class StubOpsAutonomyClient : IConsoleOpsAutonomyClient
    {
        public OperateSectionResult<OpsAutonomySnapshot> LoadResult { get; init; } =
            OperateSectionResult<OpsAutonomySnapshot>.Denied(OperateSectionStatus.Unsupported, "n/a");

        public OperateSectionResult<OpsAutonomyPolicyResponse> PolicyResult { get; init; } =
            OperateSectionResult<OpsAutonomyPolicyResponse>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public OperateSectionResult<OpsAutonomySnapshot>? ReloadResult { get; init; }

        public OperateSectionResult<OpsAutonomySettingsResponse> SettingsResult { get; init; } =
            OperateSectionResult<OpsAutonomySettingsResponse>.Denied(OperateSectionStatus.Unavailable, "n/a");

        public List<(string Rule, string Mode, string Reason)> PolicyCalls { get; } = [];

        public List<bool> KillSwitchCalls { get; } = [];

        public int LoadCalls { get; private set; }

        public Task<OperateSectionResult<OpsAutonomySnapshot>> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            return Task.FromResult(LoadCalls > 1 && ReloadResult is not null ? ReloadResult : LoadResult);
        }

        public Task<OperateSectionResult<OpsAutonomyPolicyResponse>> SetPolicyModeAsync(
            string rule,
            string mode,
            string reason,
            CancellationToken cancellationToken = default)
        {
            PolicyCalls.Add((rule, mode, reason));
            return Task.FromResult(PolicyResult);
        }

        public Task<OperateSectionResult<OpsAutonomySettingsResponse>> SetKillSwitchAsync(
            bool enabled,
            string reason,
            CancellationToken cancellationToken = default)
        {
            KillSwitchCalls.Add(enabled);
            return Task.FromResult(SettingsResult);
        }
    }

    private sealed class StubProposalsClient : IConsoleProposalsClient
    {
        public OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>> ListResult { get; init; } =
            OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed([]);

        public Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
            string? status = null,
            string? kind = null,
            string? requestedBy = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ListResult);

        public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(
            string proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Missing, "n/a"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(
            string proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden, "n/a"));

        public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(
            string proposalId,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<ConsoleProposalDetail>.Denied(OperateSectionStatus.Forbidden, "n/a"));
    }
}
