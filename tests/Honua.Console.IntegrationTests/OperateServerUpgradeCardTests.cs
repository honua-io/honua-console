using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free bUnit coverage for the server-version upgrade card. The card binds to a
/// live honua-server capability manifest for version detection (charter section 11);
/// these tests drive it through the test/demo <see cref="InMemoryConsoleServerVersionClient"/>
/// and <see cref="InMemoryConsoleDeployApprovalClient"/> to exercise:
///   - the happy path (older target detected -> upgrade available -> proceed enabled),
///   - the version-mismatch gate (already-current / downgrade -> proceed disabled), and
///   - the governed outcome surface (rollback) when an upgrade operation is in flight,
/// all without a backend. The gitops plumbing must stay behind a Details toggle.
/// </summary>
public sealed class OperateServerUpgradeCardTests
{
    private static ServerVersionInfo Version(string version) => new(
        ServerVersion: version,
        ApiVersion: "v1",
        MetadataApiVersion: "v2",
        MetadataSchemaVersion: "2",
        DeploymentEnvironment: "staging",
        ObservedAt: DateTimeOffset.UtcNow);

    private static Bunit.TestContext NewContext(
        IConsoleServerVersionClient versionClient,
        IConsoleDeployApprovalClient? approvalClient = null)
    {
        var ctx = new Bunit.TestContext();
        ctx.Services.AddSingleton(versionClient);
        ctx.Services.AddSingleton(approvalClient ?? new InMemoryConsoleDeployApprovalClient());
        return ctx;
    }

    [Fact]
    public void DetectsCurrentVersion_AndShowsUpgradeAvailable_WhenTargetIsBehind()
    {
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(Version("1.4.2")));

        var card = ctx.Render<OperateServerUpgradeCard>();

        card.WaitForAssertion(
            () => Assert.Contains("1.4.2", card.Find("[data-detected-version]").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // Enter a newer target version -> the path renders "upgrade available" and Plan is enabled.
        card.Find("[data-target-version]").Input("1.5.0");

        card.WaitForAssertion(
            () =>
            {
                Assert.Equal("upgrade available", card.Find("[data-upgrade-state]").TextContent.Trim());
                Assert.False(card.Find("[data-plan-upgrade]").HasAttribute("disabled"));
                // No gate callout for a real upgrade.
                Assert.Empty(card.FindAll("[data-upgrade-gate]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void VersionMismatchGate_BlocksProceed_WhenAlreadyCurrent()
    {
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(Version("1.5.0")));

        var card = ctx.Render<OperateServerUpgradeCard>();

        card.WaitForAssertion(
            () => Assert.NotNull(card.Find("[data-target-version]")),
            TimeSpan.FromSeconds(5));

        card.Find("[data-target-version]").Input("1.5.0");

        card.WaitForAssertion(
            () =>
            {
                Assert.Equal("already current", card.Find("[data-upgrade-state]").TextContent.Trim());
                // The gate callout is shown and the Plan button is disabled.
                Assert.NotNull(card.Find("[data-upgrade-gate]"));
                Assert.True(card.Find("[data-plan-upgrade]").HasAttribute("disabled"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void VersionMismatchGate_BlocksProceed_WhenRequestedIsOlder_Downgrade()
    {
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(Version("1.6.0")));

        var card = ctx.Render<OperateServerUpgradeCard>();

        card.WaitForAssertion(
            () => Assert.NotNull(card.Find("[data-target-version]")),
            TimeSpan.FromSeconds(5));

        card.Find("[data-target-version]").Input("1.5.0");

        card.WaitForAssertion(
            () =>
            {
                Assert.Equal("newer than requested", card.Find("[data-upgrade-state]").TextContent.Trim());
                Assert.Contains("downgrade", card.Find("[data-upgrade-gate]").TextContent, StringComparison.OrdinalIgnoreCase);
                Assert.True(card.Find("[data-plan-upgrade]").HasAttribute("disabled"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenVersionUndetectable_RendersMissingBinding_AndNoUpgradeForm()
    {
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(
            OperateSectionStatus.Unsupported,
            "Server-version detection is not configured for this Console build."));

        var card = ctx.Render<OperateServerUpgradeCard>();

        card.WaitForAssertion(
            () =>
            {
                Assert.Contains("not configured", card.Markup, StringComparison.OrdinalIgnoreCase);
                // No version-entry form is offered against unknown data.
                Assert.Empty(card.FindAll("[data-target-version]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void HidesGitOpsPlumbing_BehindDetailsToggle()
    {
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(Version("1.4.2")));

        var card = ctx.Render<OperateServerUpgradeCard>();

        card.WaitForAssertion(
            () =>
            {
                // The plumbing copy lives inside a <details> element, not on the primary surface.
                var details = card.Find("details.operate-upgrade-details");
                Assert.Contains("gitops", details.TextContent, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("/api/v1/capabilities/manifest", details.TextContent, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void WhenUpgradeOperationInFlight_RendersGovernedOutcomePanel_WithRollback()
    {
        var approvalClient = new InMemoryConsoleDeployApprovalClient([SubmittedUpgrade("upgrade-op")]);
        using var ctx = NewContext(new InMemoryConsoleServerVersionClient(Version("1.4.2")), approvalClient);

        var card = ctx.Render<OperateServerUpgradeCard>(p => p
            .Add(x => x.UpgradeOperationId, "upgrade-op"));

        // The governed outcome surface (the shared approval panel) is shown for the in-flight
        // upgrade, offering rollback. The data-affecting rollback must require explicit confirm.
        card.WaitForAssertion(
            () => Assert.NotNull(card.Find("button.operate-deploy-rollback-button")),
            TimeSpan.FromSeconds(5));

        card.Find("button.operate-deploy-rollback-button").Click();
        card.WaitForAssertion(
            () => Assert.NotNull(card.Find(".operate-deploy-confirm")),
            TimeSpan.FromSeconds(5));

        card.Find("button.console-button-danger").Click();
        card.WaitForAssertion(
            () => Assert.Contains("rollback requested", card.Markup, StringComparison.Ordinal),
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
        PrUrl: "https://git.example/pr/upgrade",
        CommitSha: "def456",
        Evidence: [new DeployOperationEvidenceLink("deploy-plan", "plan:upgrade", null)],
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
}
