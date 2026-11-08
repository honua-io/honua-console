using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class MissingBindingCompletenessCrossCuttingTests
{
    [Fact]
    public void OperateTemporal_WithNoServer_RendersMissingBinding_AndNoFabricatedData()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ITemporalCapabilityClient, UnsupportedTemporalCapabilityClient>();
        // Advertise the gated capability so the missing-binding invariant under test is reached (the
        // capability-manifest gate otherwise renders the "unsupported" state first).
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Temporal capability is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No data grid is fabricated when unbound.
        Assert.DoesNotContain("<table", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperatePublishing_WithNoServer_RendersMissingBinding_AndNoFabricatedMatrix()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IPublishingWorkspaceDataSource>(new UnsupportedPublishingWorkspaceDataSource());
        ctx.Services.AddSingleton<IServiceLayerPublishOperation>(new UnsupportedServiceLayerPublishOperation());
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = ctx.Render<OperatePublishingPage>();

        page.WaitForAssertion(
            () => Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Contains("Honua:Server:BaseUrl", page.Markup, StringComparison.Ordinal);
        // No publication projection is fabricated when unbound.
        Assert.DoesNotContain("Publishing formats", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Publishing review", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OperateDeploy_WithNoServer_RendersMissingBinding_AndNoFabricatedUpgrade()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IConsoleServerVersionClient>(new UnsupportedConsoleServerVersionClient());
        ctx.Services.AddSingleton<IConsoleDeployApprovalClient>(new UnsupportedConsoleDeployApprovalClient());
        ctx.Services.AddSingleton<IConsoleGitOpsReleaseClient>(new UnboundReleaseClient());
        // console#290: "All deploy operations" and the platform-release converge card.
        ctx.Services.AddSingleton<IConsoleDeployOperationsClient>(new UnsupportedConsoleDeployOperationsClient());
        // The server-version upgrade card asserted below is ungated (operate floor); advertise the gated
        // cross-environment-promotion capability so the manifest gate resolves and the page renders.
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);

        var page = ctx.Render<OperateDeployPage>();

        page.WaitForAssertion(
            () =>
            {
                // The upgrade card surfaces the unsupported server-version binding, not an upgrade form.
                Assert.Contains("not configured", page.Markup, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(page.FindAll("[data-target-version]"));
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ShareManage_WithNoServer_RendersMissingBinding_AndNoFabricatedPanels()
    {
        using var ctx = new Bunit.BunitContext();
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = Bunit.JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<IShareAccessDataSource>(new UnsupportedShareAccessDataSource());
        ctx.Services.AddSingleton<IConsoleCatalogClient>(new UnsupportedConsoleCatalogClient());
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", "item-cross-cutting"));

        var page = ctx.Render<ShareManagePage>();

        page.WaitForAssertion(
            () => Assert.Contains("Share access API is not bound", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        // No share-management panels are fabricated when unbound.
        Assert.DoesNotContain("data-share-access", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("data-share-panel=\"public-link\"", page.Markup, StringComparison.Ordinal);
    }

    // An unbound release client: no active profile means the server-owned release data is a
    // missing-binding result, never fabricated. Mirrors what the production HTTP client returns
    // when no environment profile is selected.
    private sealed class UnboundReleaseClient : IConsoleGitOpsReleaseClient
    {
        public Task<OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>> GetReleaseProposalsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<IReadOnlyList<GitOpsReleaseProposal>>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));

        public Task<OperateSectionResult<GitOpsReleaseProposal>> GetReleaseProposalAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseProposal>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));

        public Task<OperateSectionResult<GitOpsReleaseDetail>> GetReleaseDetailAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsReleaseDetail>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));

        public Task<OperateSectionResult<GitOpsCoordinatedRelease>> GetCoordinatedReleaseAsync(
            string releasePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(OperateSectionResult<GitOpsCoordinatedRelease>.Denied(
                OperateSectionStatus.Unavailable,
                "No active environment profile is selected."));
    }
}
