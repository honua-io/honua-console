using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateSupportAdvisoriesPageTests
{
    [Fact]
    public void UnconfiguredPortalRendersNeutralSetupGuidanceWithoutTables()
    {
        using var context = CreateContext(new UnsupportedConsoleSupportPortalClient());
        var page = context.Render<OperateSupportAdvisoriesPage>();

        page.WaitForAssertion(() => Assert.Contains("HonuaSupport:BaseUrl", page.Markup));
        Assert.Equal("Support & Security Advisories", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll("[data-advisories-table]"));
        Assert.Empty(page.FindAll("[data-fleet-table]"));
        Assert.Empty(page.FindAll("[data-tickets-table]"));
    }

    [Fact]
    public void PopulatedPortalRendersAdvisoriesFleetTicketsAndSafeLinks()
    {
        var client = new StubSupportPortalClient();
        using var context = CreateContext(client);
        var page = context.Render<OperateSupportAdvisoriesPage>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-advisories-table]")));
        Assert.Single(page.FindAll("[data-kev-badge]"));
        Assert.Contains("HONUA-SA-2026-001", page.Markup);
        Assert.Contains("CVE-2026-10001", page.Markup);
        Assert.Contains("prod-west-1", page.Markup);
        Assert.Contains("ticket-42", page.Markup);

        var notesLinks = page.FindAll("[data-advisory-notes-link]");
        Assert.Single(notesLinks);
        Assert.Equal("https://honua.io/advisories/2026-001", notesLinks[0].GetAttribute("href"));
        Assert.DoesNotContain("javascript:", page.Markup);
    }

    [Fact]
    public async Task PreflightSubmissionApproveAndRevokeUpdateSurface()
    {
        var client = new StubSupportPortalClient();
        using var context = CreateContext(client);
        var page = context.Render<OperateSupportAdvisoriesPage>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-tickets-table]")));

        await page.InvokeAsync(() =>
        {
            page.Find("textarea").Change("Elevated 500s on FeatureServer");
            page.Find("[data-run-preflight]").Click();
        });
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-preflight-result]")));
        Assert.Contains("Upgrade server to 1.4.2", page.Markup);

        await page.InvokeAsync(() =>
        {
            page.FindAll("input:not([type=checkbox])")[1].Change("Diagnose FeatureServer 500s");
            page.Find("[data-submit-support-ticket]").Click();
        });
        page.WaitForAssertion(() => Assert.Contains("Created support ticket ticket-new", page.Find("[data-support-action-status]").TextContent));
        Assert.Single(client.CreatedTickets);

        await page.InvokeAsync(() => page.Find("[data-approve-ticket='ticket-42']").Click());
        page.WaitForAssertion(() => Assert.Contains("Approved access for ticket ticket-42", page.Find("[data-support-action-status]").TextContent));
        Assert.Single(client.ApprovedTicketIds);

        await page.InvokeAsync(() => page.Find("[data-revoke-ticket-access='ticket-42']").Click());
        page.WaitForAssertion(() => Assert.Contains("Revoked active support access for ticket ticket-42", page.Find("[data-support-action-status]").TextContent));
        Assert.Single(client.RevokedTicketIds);
    }

    private static BunitContext CreateContext(IConsoleSupportPortalClient client)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        context.Services.AddSingleton(client);
        var profiles = new InMemoryConsoleEnvironmentProfileStore([]);
        var sessions = new InMemoryConsoleAccountSessionStore();
        var observability = new HttpConsoleOperateObservabilityClient(new HttpClient(), profiles, sessions);
        context.Services.AddSingleton(new ConsoleSupportContextBuilder(profiles, sessions, observability));
        return context;
    }

    private sealed class StubSupportPortalClient : IConsoleSupportPortalClient
    {
        public List<CreateSupportTicketRequest> CreatedTickets { get; } = [];

        public List<string> ApprovedTicketIds { get; } = [];

        public List<string> RevokedTicketIds { get; } = [];

        public Task<SupportPortalResult<SupportPreflightResponse>> RunPreflightAsync(
            SupportPreflightRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportPortalResult<SupportPreflightResponse>.Allowed(new SupportPreflightResponse
            {
                ProductArea = "server",
                IssueKind = "compatibility",
                ClassificationProvenance = "inferred",
                Compatibility = new SupportPreflightCompatibilityResponse
                {
                    Deflected = true,
                    Summary = "Upgrade server to 1.4.2 to resolve FeatureServer mismatch.",
                    Confidence = "high",
                    GuidedCommands = ["honua upgrade --version 1.4.2"]
                },
                KnownIssues =
                [
                    new SupportPreflightKnownIssueResponse
                    {
                        Component = "honua-server",
                        FixedInServerVersion = "1.4.2",
                        PatchStage = "released"
                    }
                ],
                RedactionPreview = new SupportPreflightRedactionPreviewResponse
                {
                    HeadersDropped = 3,
                    BodiesRedacted = 1,
                    ScopedKeyWillBeBrokered = false
                }
            }));

        public Task<SupportPortalResult<IReadOnlyList<SupportTicketResponse>>> ListTicketsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportPortalResult<IReadOnlyList<SupportTicketResponse>>.Allowed(
            [
                SampleTicket("ticket-42")
            ]));

        public Task<SupportPortalResult<SupportTicketResponse>> CreateTicketAsync(
            CreateSupportTicketRequest request,
            CancellationToken cancellationToken = default)
        {
            CreatedTickets.Add(request);
            return Task.FromResult(SupportPortalResult<SupportTicketResponse>.Allowed(SampleTicket("ticket-new")));
        }

        public Task<SupportPortalResult<SupportTicketResponse>> ApproveTicketAsync(
            string ticketId,
            SupportApproveTicketRequest request,
            CancellationToken cancellationToken = default)
        {
            ApprovedTicketIds.Add(ticketId);
            var approval = new SupportTicketApproval { ApprovedBy = "operator-a", ApprovedAt = DateTimeOffset.UtcNow };
            return Task.FromResult(SupportPortalResult<SupportTicketResponse>.Allowed(SampleTicket(ticketId, approval)));
        }

        public Task<SupportPortalResult<SupportRevokeAccessResponse>> RevokeAccessAsync(
            string ticketId,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            RevokedTicketIds.Add(ticketId);
            return Task.FromResult(SupportPortalResult<SupportRevokeAccessResponse>.Allowed(new SupportRevokeAccessResponse
            {
                TicketId = ticketId,
                ScopedCredentialRevoked = true,
                Ticket = SampleTicket(ticketId)
            }));
        }

        public Task<SupportPortalResult<SupportSecurityAdvisoriesResponse>> ListAdvisoriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportPortalResult<SupportSecurityAdvisoriesResponse>.Allowed(new SupportSecurityAdvisoriesResponse
            {
                Count = 2,
                Advisories =
                [
                    new SupportSecurityAdvisoryItem
                    {
                        AdvisoryId = "HONUA-SA-2026-001",
                        CveId = "CVE-2026-10001",
                        Title = "Token audience validation hardening",
                        Severity = "critical",
                        CisaKev = true,
                        Component = "honua-server",
                        VexStatus = "affected",
                        CustomerStatus = "affected",
                        FixedVersion = "1.4.2",
                        UpgradeCommand = "helm upgrade honua honua/honua-server --version 1.4.2",
                        ReleaseNotesUrl = "https://honua.io/advisories/2026-001",
                        Instances =
                        [
                            new SupportAdvisoryInstanceView
                            {
                                InstanceId = "prod-west-1",
                                Status = "affected",
                                ObservedVersion = "1.4.1"
                            }
                        ],
                        UpdatedAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z")
                    },
                    new SupportSecurityAdvisoryItem
                    {
                        AdvisoryId = "HONUA-SA-2026-002",
                        Title = "Unsafe link test",
                        Severity = "low",
                        Component = "honua-console",
                        VexStatus = "not_affected",
                        ReleaseNotesUrl = "javascript:alert(1)",
                        UpdatedAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z")
                    }
                ]
            }));

        public Task<SupportPortalResult<SupportFleetInstancesResponse>> ListFleetInstancesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupportPortalResult<SupportFleetInstancesResponse>.Allowed(new SupportFleetInstancesResponse
            {
                Count = 1,
                Instances =
                [
                    new SupportFleetInstanceItem
                    {
                        CustomerId = "cust-1",
                        InstanceId = "prod-west-1",
                        Component = "honua-server",
                        Version = "1.4.1",
                        ArtifactDigest = "sha256:0123456789abcdef0123456789abcdef",
                        EnvKind = "production",
                        LastSource = "telemetry",
                        FirstSeenAt = DateTimeOffset.Parse("2026-11-20T00:00:00Z"),
                        LastSeenAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z")
                    }
                ]
            }));

        private static SupportTicketResponse SampleTicket(string id, SupportTicketApproval? approval = null) => new()
        {
            Id = id,
            CustomerId = "cust-1",
            Severity = "sev2",
            Environment = "production",
            Service = "honua-server",
            Symptoms = "Elevated 500s",
            RequestedAction = "Diagnose FeatureServer",
            AllowedAccessMode = "operator-scoped",
            TtlMinutes = 30,
            Phase = "triaging",
            CustomerStatus = "in-progress",
            Approval = approval,
            CreatedAt = DateTimeOffset.Parse("2026-11-27T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-11-27T00:05:00Z")
        };
    }
}
