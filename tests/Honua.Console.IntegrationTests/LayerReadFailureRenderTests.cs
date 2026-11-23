using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// A failed layer projection read must not read as "Layer Not Found": the layer may exist.
public sealed class LayerReadFailureRenderTests
{
    public enum ConsoleLayout { Legacy, V4 }

    private const string ResourceId = "conn-1-layer-1";
    private const string NotFoundTitle = "Layer Not Found";

    private static readonly OperateCapabilityState Timeout =
        new("Services", OperateLayerLookup.UnavailableState, "GET /api/v1/admin/services", "net_http_request_timedout, 30");

    private static readonly OperateCapabilityState ServerError =
        new("Services", OperateLayerLookup.UnavailableState, "GET /api/v1/admin/services", "Service unavailable. HTTP 503.")
        {
            StatusCode = 503,
        };

    private static readonly OperateCapabilityState Forbidden =
        new("Services", OperateLayerLookup.MissingPermissionState, "GET /api/v1/admin/services", "Forbidden. HTTP 403.")
        {
            StatusCode = 403,
        };

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void FilterPage_TimedOutRead_OffersRetryAndRecovers(ConsoleLayout layout) =>
        AssertRetryRecovers<OperateLayerFilterPage>(layout);

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void RelationshipsPage_TimedOutRead_OffersRetryAndRecovers(ConsoleLayout layout) =>
        AssertRetryRecovers<OperateLayerRelationshipsPage>(layout);

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void EditingPolicyPage_TimedOutRead_OffersRetryAndRecovers(ConsoleLayout layout) =>
        AssertRetryRecovers<OperateLayerMetadataPage>(layout);

    [Fact]
    public void DetailPage_TimedOutRead_OffersRetryThatReadsAgain()
    {
        var data = new FailingThenLayerDataSource(Timeout, failures: 2);
        using var ctx = CreateContext(data, ConsoleLayout.V4);

        var page = ctx.Render<OperateLayerDetailPage>(p => p.Add(x => x.ResourceId, ResourceId));
        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
        Assert.DoesNotContain(NotFoundTitle, page.Markup, StringComparison.Ordinal);

        page.Find("[data-item-read-retry]").Click();

        page.WaitForAssertion(() => Assert.Equal(2, data.Reads));
        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
    }

    [Fact]
    public void ServerError_KeepsTheServerDetailAndOffersRetry()
    {
        using var ctx = CreateContext(new FailingThenLayerDataSource(ServerError, failures: 1), ConsoleLayout.V4);

        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
        Assert.Contains("HTTP 503", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(OperateLayerLookup.TemporaryFailureMessage, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentChangeDuringRead_KeepsItsOwnMessageAndOffersRetry()
    {
        var changed = new OperateCapabilityState(
            "Services", OperateLayerLookup.UnavailableState, "Active Environment", "The active environment changed.");
        using var ctx = CreateContext(new FailingThenLayerDataSource(changed, failures: 1), ConsoleLayout.V4);

        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
        Assert.Contains("The active environment changed.", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(OperateLayerLookup.TemporaryFailureMessage, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ForbiddenRead_ShowsPermissionRequiredWithoutRetry()
    {
        using var ctx = CreateContext(new FailingThenLayerDataSource(Forbidden, failures: 1), ConsoleLayout.V4);

        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(() => Assert.Contains("Permission required", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-item-read-retry]"));
        Assert.DoesNotContain(NotFoundTitle, page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessfulReadWithoutTheLayer_StillReadsAsNotFound()
    {
        using var ctx = CreateContext(new FailingThenLayerDataSource(failure: null, failures: 1), ConsoleLayout.V4);

        var page = ctx.Render<OperateLayerFilterPage>(p => p.Add(x => x.ResourceId, ResourceId));

        page.WaitForAssertion(() => Assert.Contains(NotFoundTitle, page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-item-read-failure]"));
    }

    private static void AssertRetryRecovers<TPage>(ConsoleLayout layout)
        where TPage : IComponent
    {
        using var ctx = CreateContext(new FailingThenLayerDataSource(Timeout, failures: 1), layout);

        var page = ctx.Render(builder =>
        {
            builder.OpenComponent<TPage>(0);
            builder.AddAttribute(1, nameof(OperateLayerFilterPage.ResourceId), ResourceId);
            builder.CloseComponent();
        });
        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
        Assert.Contains(OperateLayerLookup.TemporaryFailureMessage, page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(NotFoundTitle, page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("net_http", page.Markup, StringComparison.Ordinal);

        page.Find("[data-item-read-retry]").Click();

        page.WaitForAssertion(() => Assert.Contains("Parcels", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-item-read-failure]"));
    }

    private static BunitContext CreateContext(IOperateTransitionDataSource data, ConsoleLayout layout)
    {
        var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.AddConsoleNotifications();
        ctx.Services.AddSingleton(new ConsoleAgentContextRegistry());
        ctx.Services.AddSingleton(new ConsoleV4Options { Enabled = layout == ConsoleLayout.V4 });
        ctx.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        ctx.Services.AddSingleton<IOperateTransitionDataSource>(data);
        ctx.Services.AddSingleton<IConsoleLayerFilterOperation, UnsupportedConsoleLayerFilterOperation>();
        ctx.Services.AddSingleton<IConsoleLayerRelationshipsOperation, UnsupportedConsoleLayerRelationshipsOperation>();
        ctx.Services.AddSingleton<IConsoleLayerMetadataOperation, UnsupportedConsoleLayerMetadataOperation>();
        ctx.Services.AddSingleton<IConsoleLayerFieldsOperation, UnsupportedConsoleLayerFieldsOperation>();
        return ctx;
    }

    // Answers the first reads with an empty projection (failed when a failure is given), then with the layer.
    private sealed class FailingThenLayerDataSource(OperateCapabilityState? failure, int failures) : IOperateTransitionDataSource
    {
        private readonly FixedServicesDataSource _layer = FixedServicesDataSource.WithLayer(
            new OperateServiceLayer(1, "Parcels", "polygon", ResourceId, "parcels"));

        public int Reads { get; private set; }

        public async Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            if (Reads > failures)
            {
                return await _layer.GetWorkspaceAsync(cancellationToken);
            }

            return new OperateTransitionWorkspace([], [], [], [], failure is null ? [] : [failure]);
        }

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            _layer.FindConnectionAsync(connectionId, cancellationToken);

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            _layer.FindResourceEditAsync(resourceId, cancellationToken);

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            _layer.FindServiceAsync(serviceName, cancellationToken);
    }
}
