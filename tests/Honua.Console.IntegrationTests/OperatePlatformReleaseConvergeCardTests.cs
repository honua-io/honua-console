using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperatePlatformReleaseConvergeCardTests
{
    [Fact]
    public void NoDeclaredRelease_RendersHonestEmptyState()
    {
        var client = new InMemoryConsoleDeployOperationsClient();

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(client);

        var card = ctx.Render<OperatePlatformReleaseConvergeCard>();

        card.WaitForAssertion(
            () => Assert.Contains("No platform release declared", card.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void DeclaredButSkewedRelease_ShowsSkewedBadge_AndConvergeUnavailableByDefault()
    {
        var preflight = OperateSectionResult<DeployPreflightView>.Allowed(new DeployPreflightView(
            Status: new OperateStatus("warning", "not ready"),
            ReadyForCoordinatedDeploy: false,
            Message: "not ready",
            DiagnosticsIncluded: true,
            UpgradeRequired: false,
            PlanAvailable: true,
            PendingScripts: [],
            ExecutedButNotDiscoveredScripts: [],
            PlanError: null,
            DatabaseCompatible: true,
            DatabaseWarnings: [],
            DatabaseErrorMessage: null,
            PlatformReleaseDeclared: true,
            PlatformReleaseCoVersioned: false,
            SkewedPlaneIds: ["worker-2"]));
        var client = new InMemoryConsoleDeployOperationsClient(preflight: preflight);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(client);

        var card = ctx.Render<OperatePlatformReleaseConvergeCard>();

        card.WaitForAssertion(
            () =>
            {
                Assert.Equal("skewed", card.Find("[data-coversion-badge]").TextContent.Trim());
                Assert.Contains("worker-2", card.Markup, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        card.Find("[data-converge]").Click();

        // honua-server#2564 is not merged: every server today returns Unsupported.
        card.WaitForAssertion(
            () => Assert.Contains("not available on the connected server", card.Markup, StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SeededConvergeSuccess_ShowsProposalChip_NeverDirectExecute()
    {
        var preflight = OperateSectionResult<DeployPreflightView>.Allowed(new DeployPreflightView(
            Status: new OperateStatus("warning", "not ready"),
            ReadyForCoordinatedDeploy: false,
            Message: "not ready",
            DiagnosticsIncluded: true,
            UpgradeRequired: false,
            PlanAvailable: true,
            PendingScripts: [],
            ExecutedButNotDiscoveredScripts: [],
            PlanError: null,
            DatabaseCompatible: true,
            DatabaseWarnings: [],
            DatabaseErrorMessage: null,
            PlatformReleaseDeclared: true,
            PlatformReleaseCoVersioned: false,
            SkewedPlaneIds: ["worker-2"]));
        var converge = OperateSectionResult<PlatformReleaseConvergeView>.Allowed(new PlatformReleaseConvergeView(
            Targets: [],
            ProposalId: "prop-converge-1",
            Message: null));
        var client = new InMemoryConsoleDeployOperationsClient(preflight: preflight, converge: converge);

        using var ctx = new BunitContext();
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(client);

        var card = ctx.Render<OperatePlatformReleaseConvergeCard>();

        card.WaitForAssertion(() => Assert.NotNull(card.Find("[data-converge]")), TimeSpan.FromSeconds(5));
        card.Find("[data-converge]").Click();

        card.WaitForAssertion(
            () =>
            {
                Assert.NotNull(card.Find("[data-converge-proposal]"));
                Assert.Contains("prop-converge-1", card.Markup, StringComparison.Ordinal);
                Assert.Contains("approve it in the inbox", card.Markup, StringComparison.OrdinalIgnoreCase);
            },
            TimeSpan.FromSeconds(5));
    }
}
