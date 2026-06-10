using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// trust-visibility (#166) + trust-data relay (honua-support#23): Docker-free
/// render coverage for the escalation rationale card, the live remote-session
/// banner, and the diagnosis scorecard checklist on the ticket detail page.
/// Drives the page through a fake <see cref="IConsoleSupportTicketClient"/>
/// serving a single ticket response, covering BOTH the authoritative `trust.*`
/// path and the absent-`trust` best-effort fallback (so the consume side is safe
/// to merge before support#23 ships).
/// </summary>
public sealed class SupportTicketDetailTrustRenderTests
{
    [Fact]
    public void Detail_WhenEscalated_RendersRationaleHeadlineTriggerAndSignal()
    {
        var ticket = BaseTicket() with
        {
            EscalationTier = 2,
            EscalatedAt = DateTimeOffset.Parse("2026-06-05T13:00:00Z"),
            EscalatedBy = "triage.bot",
            Diagnosis = Diagnosis() with
            {
                Escalation = new SupportEscalationRationale
                {
                    Justification = "Guided fix did not clear the error rate within the validation window.",
                    AccessScope = "operator-scoped",
                    TtlMinutes = 30,
                    RollbackIntent = "rollback-on-fail",
                    Trigger = "error-rate-not-cleared",
                    Signal = "http_5xx_rate"
                }
            }
        };

        var markup = Render(ticket);

        Assert.Contains("Why this escalated", markup, StringComparison.Ordinal);
        Assert.Contains("error rate not cleared", markup, StringComparison.Ordinal);
        Assert.Contains("→ escalating", markup, StringComparison.Ordinal);
        Assert.Contains("http_5xx_rate", markup, StringComparison.Ordinal);
        Assert.Contains("Guided fix did not clear the error rate", markup, StringComparison.Ordinal);
        Assert.Contains("Tier 2", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_WhenNotEscalated_OmitsRationaleCard()
    {
        var markup = Render(BaseTicket());

        Assert.DoesNotContain("Why this escalated", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_OperatorScopedSession_RendersActiveBannerWithCountdownAndCustomerVisible()
    {
        var ticket = BaseTicket() with
        {
            AllowedAccessMode = "operator-scoped",
            TtlMinutes = 45,
            AccessBoundary = new SupportAccessBoundary
            {
                FirstEscalationStep = "read-only",
                OperatorScopedRequirement = "ticket-scoped approval required",
                Exclusions = ["no standing cloud credentials"]
            },
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var markup = Render(ticket);

        Assert.Contains("Remote session", markup, StringComparison.Ordinal);
        Assert.Contains("remaining", markup, StringComparison.Ordinal);
        Assert.Contains("this activity is shown to the customer", markup, StringComparison.Ordinal);
        Assert.Contains("no standing cloud credentials", markup, StringComparison.Ordinal);
        Assert.Contains("View actions", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_ReadOnlyAccess_RendersIdleSessionBanner()
    {
        var ticket = BaseTicket() with { AllowedAccessMode = "read-only", TtlMinutes = 60 };

        var markup = Render(ticket);

        Assert.Contains("No elevated remote session is open", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_WhenScorecardPresent_RendersPassFailAndCriteria()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "pass": false,
              "score": "3/5",
              "criteria": { "config-valid": true, "error-rate-cleared": false },
              "failureModes": ["error rate above threshold"]
            }
            """);
        var ticket = BaseTicket() with
        {
            Diagnosis = Diagnosis() with { Scorecard = doc.RootElement.Clone() }
        };

        var markup = Render(ticket);

        Assert.Contains("Diagnosis scorecard", markup, StringComparison.Ordinal);
        Assert.Contains("FAIL", markup, StringComparison.Ordinal);
        Assert.Contains("3/5", markup, StringComparison.Ordinal);
        Assert.Contains("config valid", markup, StringComparison.Ordinal);
        Assert.Contains("error rate cleared", markup, StringComparison.Ordinal);
        Assert.Contains("error rate above threshold", markup, StringComparison.Ordinal);
    }

    // ===== trust-data relay (honua-support#23): authoritative `trust` path =====

    [Fact]
    public void Detail_AuthoritativeTrustEscalation_PrefersTrustOverLooseRationale()
    {
        var ticket = BaseTicket() with
        {
            EscalationTier = 2,
            // Loose diagnosis rationale carries STALE values that must NOT win.
            Diagnosis = Diagnosis() with
            {
                Escalation = new SupportEscalationRationale
                {
                    Justification = "stale loose justification",
                    Trigger = "stale-trigger",
                    Signal = "stale_signal"
                }
            },
            Trust = new SupportTicketTrust
            {
                Escalation = new SupportTrustEscalation
                {
                    Escalated = true,
                    Trigger = "error-rate-not-cleared",
                    Signal = "http_5xx_rate",
                    Justification = "Authoritative: guided fix did not clear the error rate.",
                    AccessScope = "operator-scoped",
                    RollbackIntent = "rollback-on-fail",
                    ApprovedBy = "oncall.lead",
                    ApprovedAt = DateTimeOffset.Parse("2026-06-05T13:05:00Z")
                }
            }
        };

        var markup = Render(ticket);

        Assert.Contains("Why this escalated", markup, StringComparison.Ordinal);
        Assert.Contains("error rate not cleared", markup, StringComparison.Ordinal);
        Assert.Contains("http_5xx_rate", markup, StringComparison.Ordinal);
        Assert.Contains("Authoritative: guided fix did not clear", markup, StringComparison.Ordinal);
        Assert.Contains("oncall.lead", markup, StringComparison.Ordinal);
        // The stale loose values must not leak through.
        Assert.DoesNotContain("stale loose justification", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("stale_signal", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_AuthoritativeTrustSession_UsesExpiresAtCustomerVisibleAndActive()
    {
        var ticket = BaseTicket() with
        {
            // Loose fields would imply NO session (read-only); trust says otherwise.
            AllowedAccessMode = "read-only",
            TtlMinutes = 0,
            Trust = new SupportTicketTrust
            {
                DelegatedSession = new SupportTrustDelegatedSession
                {
                    Mode = "operator-scoped",
                    EstablishedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20),
                    CustomerVisible = true,
                    Active = true
                }
            }
        };

        var markup = Render(ticket);

        Assert.Contains("Remote session", markup, StringComparison.Ordinal);
        Assert.Contains("remaining", markup, StringComparison.Ordinal);
        Assert.Contains("Expires", markup, StringComparison.Ordinal);
        // Authoritative path drops the best-effort caption.
        Assert.DoesNotContain("best-effort expiry", markup, StringComparison.Ordinal);
        Assert.Contains("this activity is shown to the customer", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_AuthoritativeTrustSession_DisabledModeRendersIdle()
    {
        var ticket = BaseTicket() with
        {
            AllowedAccessMode = "operator-scoped",
            TtlMinutes = 45,
            Trust = new SupportTicketTrust
            {
                DelegatedSession = new SupportTrustDelegatedSession
                {
                    Mode = "disabled",
                    CustomerVisible = false,
                    Active = false
                }
            }
        };

        var markup = Render(ticket);

        // `disabled` is treated as no session; falls through to the idle copy.
        Assert.Contains("No elevated remote session is open", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_AuthoritativeTrustScorecard_RendersStructuredCriteriaAndEvidence()
    {
        var ticket = BaseTicket() with
        {
            // Loose free-form scorecard carries STALE values that must NOT win.
            Diagnosis = Diagnosis() with
            {
                Scorecard = JsonDocument.Parse("""{ "pass": true, "score": "stale" }""").RootElement.Clone()
            },
            Trust = new SupportTicketTrust
            {
                Scorecard = new SupportTrustScorecard
                {
                    OverallResult = "fail",
                    Score = 3,
                    Confidence = "high",
                    Criteria =
                    [
                        new SupportTrustCriterion { Name = "config-valid", Passed = true },
                        new SupportTrustCriterion { Name = "error-rate-cleared", Passed = false }
                    ],
                    FailureModes = ["error rate above threshold"],
                    EvidenceRefs = ["evidence://run/42"]
                }
            }
        };

        var markup = Render(ticket);

        Assert.Contains("Diagnosis scorecard", markup, StringComparison.Ordinal);
        Assert.Contains("FAIL", markup, StringComparison.Ordinal);
        Assert.Contains("high confidence", markup, StringComparison.Ordinal);
        Assert.Contains("config valid", markup, StringComparison.Ordinal);
        Assert.Contains("error rate cleared", markup, StringComparison.Ordinal);
        Assert.Contains("error rate above threshold", markup, StringComparison.Ordinal);
        Assert.Contains("evidence://run/42", markup, StringComparison.Ordinal);
        // Stale loose pass result must not leak through.
        Assert.DoesNotContain("PASS", markup, StringComparison.Ordinal);
    }

    private static string Render(SupportTicketResponse ticket)
    {
        using var ctx = new Bunit.BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleSupportTicketClient>(new FakeSupportTicketClient(ticket));

        var page = ctx.Render<SupportTicketDetailPage>(
            p => p.Add(c => c.TicketId, ticket.Id));
        page.WaitForAssertion(
            () => Assert.Contains("Status", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // Snapshot the markup while the render tree is still alive; the page runs a
        // background poll loop, so disposing the context after capture is correct.
        return page.Markup;
    }

    private static SupportTicketResponse BaseTicket() =>
        new()
        {
            Id = "ticket-9",
            CustomerId = "honua-prod",
            Severity = "sev2",
            Environment = "production",
            Service = "honua-server",
            Symptoms = "elevated 500s",
            RequestedAction = "diagnose",
            AllowedAccessMode = "read-only",
            Phase = "diagnosed",
            CustomerStatus = "in progress",
            CreatedAt = DateTimeOffset.Parse("2026-06-05T12:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-06-05T12:30:00Z")
        };

    private static SupportTicketDiagnosis Diagnosis() =>
        new()
        {
            Summary = "Error rate elevated after deploy.",
            Confidence = "high",
            Mode = "guided-fix",
            DiagnosedAt = DateTimeOffset.Parse("2026-06-05T12:20:00Z")
        };

    private sealed class FakeSupportTicketClient(SupportTicketResponse ticket) : IConsoleSupportTicketClient
    {
        public Task<SupportTicketResult> CreateTicketAsync(
            CreateSupportTicketRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportTicketResult.Allowed(ticket));

        public Task<SupportTicketResult> GetTicketAsync(
            string ticketId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportTicketResult.Allowed(ticket));
    }
}
