using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// A failed or pending read must not read as "Not Found" on the service, connection and dataset pages:
// the item may exist, so the page shows the failure with Retry, as the layer pages do.
public sealed class DetailReadFailureRenderTests
{
    public enum ConsoleLayout { Legacy, V4 }

    private sealed record RecoveryExpectation(string NotFound, string Message, string LoadedSelector);

    private const string ServiceName = "parcels-service";
    private const string ResourceId = "parcels";

    private static readonly OperateCapabilityState TooManyRequests =
        new("Services", OperateLayerLookup.UnavailableState, "GET /api/v1/admin/services", "Too many requests. HTTP 429.")
        {
            StatusCode = 429,
        };

    private static readonly OperateCapabilityState ConnectionsForbidden =
        new("Connections", OperateLayerLookup.MissingPermissionState, "GET /api/v1/admin/connections", "Forbidden. HTTP 403.")
        {
            StatusCode = 403,
        };

    private static readonly OperateCapabilityState ConnectionsTimeout =
        new("Connections", OperateLayerLookup.UnavailableState, "GET /api/v1/admin/connections", "net_http_request_timedout, 30");

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void ServiceDetail_FailedRead_OffersRetryAndRecovers(ConsoleLayout layout)
    {
        var data = new FailingThenItemsDataSource(TooManyRequests, failures: 1);
        using var ctx = CreateContext(data, layout);

        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(x => x.ServiceName, ServiceName));

        AssertRetryRecovers(page, data, new("Service Not Found", "HTTP 429", "[data-service-detail]"));
    }

    // Services are also discovered through connection layers, so those read failures can hide a service too.
    [Theory]
    [InlineData("Connections")]
    [InlineData("Layers")]
    public void ServiceDetail_FailedFallbackRead_OffersRetryAndRecovers(string surface)
    {
        var data = new FailingThenItemsDataSource(ConnectionsTimeout with { Surface = surface }, failures: 1);
        using var ctx = CreateContext(data, ConsoleLayout.V4);

        var page = ctx.Render<OperateServiceDetailPage>(p => p.Add(x => x.ServiceName, ServiceName));

        AssertRetryRecovers(page, data, new("Service Not Found", OperateLayerLookup.TemporaryFailureMessage, "[data-service-detail]"));
    }

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void ConnectionDetail_FailedRead_OffersRetryAndRecovers(ConsoleLayout layout)
    {
        var data = new FailingThenItemsDataSource(ConnectionsTimeout, failures: 1);
        using var ctx = CreateContext(data, layout);

        var page = ctx.Render<OperateConnectionDetailPage>(p => p.Add(x => x.ConnectionId, ConnectionRenderTestSupport.Connection.Id));

        AssertRetryRecovers(page, data, new("Connection Not Found", OperateLayerLookup.TemporaryFailureMessage, "[data-connection-loaded]"));
        Assert.DoesNotContain("net_http", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void DatasetDetail_FailedRead_OffersRetryAndRecovers(ConsoleLayout layout)
    {
        var data = new FailingThenItemsDataSource(ConnectionsTimeout, failures: 1);
        using var ctx = CreateContext(data, layout);

        var page = ctx.Render<OperateResourceDetailPage>(p => p.Add(x => x.ResourceId, ResourceId));

        AssertRetryRecovers(page, data, new("Dataset Not Found", OperateLayerLookup.TemporaryFailureMessage, "[data-resource-detail] h1"));
        Assert.Contains("Parcels", page.Find("[data-resource-detail] h1").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConsoleLayout.Legacy)]
    [InlineData(ConsoleLayout.V4)]
    public void ConnectionDetail_PendingRead_ShowsLoadingNotNotFound(ConsoleLayout layout)
    {
        var data = new FailingThenItemsDataSource(failure: null, failures: 0)
        {
            Gate = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var ctx = CreateContext(data, layout);

        var page = ctx.Render<OperateConnectionDetailPage>(p => p.Add(x => x.ConnectionId, ConnectionRenderTestSupport.Connection.Id));

        Assert.Contains("Loading connection", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection Not Found", page.Markup, StringComparison.Ordinal);
        data.Gate.SetResult();
        page.WaitForAssertion(() => page.Find("[data-connection-loaded]"));
        Assert.DoesNotContain("Loading connection", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionDetail_ForbiddenRead_ShowsPermissionRequiredWithoutRetry()
    {
        using var ctx = CreateContext(new FailingThenItemsDataSource(ConnectionsForbidden, failures: 1), ConsoleLayout.V4);

        var page = ctx.Render<OperateConnectionDetailPage>(p => p.Add(x => x.ConnectionId, ConnectionRenderTestSupport.Connection.Id));

        page.WaitForAssertion(() => Assert.Contains("Permission required", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-item-read-retry]"));
        Assert.DoesNotContain("Connection Not Found", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionDetail_SuccessfulReadWithoutTheConnection_StillReadsAsNotFound()
    {
        using var ctx = CreateContext(new FailingThenItemsDataSource(failure: null, failures: 0), ConsoleLayout.V4);

        var page = ctx.Render<OperateConnectionDetailPage>(p => p.Add(x => x.ConnectionId, "missing"));

        page.WaitForAssertion(() => Assert.Contains("Connection Not Found", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-item-read-failure]"));
    }

    private static void AssertRetryRecovers<TPage>(
        IRenderedComponent<TPage> page, FailingThenItemsDataSource data, RecoveryExpectation expected)
        where TPage : IComponent
    {
        page.WaitForAssertion(() => page.Find("[data-item-read-retry]"));
        Assert.DoesNotContain(expected.NotFound, page.Markup, StringComparison.Ordinal);
        Assert.Contains(expected.Message, page.Markup, StringComparison.Ordinal);

        page.Find("[data-item-read-retry]").Click();

        page.WaitForAssertion(() => page.Find(expected.LoadedSelector));
        Assert.Equal(2, data.Reads);
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
        ctx.Services.AddSingleton(data);
        ctx.Services.AddSingleton<IServiceConfigurationOperation, UnsupportedServiceConfigurationOperation>();
        ctx.Services.AddSingleton<IConsoleConnectionCreateOperation, UnsupportedConsoleConnectionCreateOperation>();
        return ctx;
    }

    // Answers the first reads with an empty workspace (failed when a failure is given), then with one
    // service, connection and dataset. A gate holds every read until it is released.
    private sealed class FailingThenItemsDataSource(OperateCapabilityState? failure, int failures) : IOperateTransitionDataSource
    {
        private static readonly OperateTransitionWorkspace Items = new(
            [ConnectionRenderTestSupport.Connection],
            [new(ResourceId, "Parcels", "file", "private_change", "Ready", new OperateBlastRadius([], [], [], [], [], []), [], ["Fields"])],
            [new(ServiceName, "Parcels service", "FeatureServer", "Running", "server", [], [], [])],
            [],
            []);

        public TaskCompletionSource? Gate { get; init; }

        public int Reads { get; private set; }

        public async Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            var read = Reads;
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return read > failures ? Items : new OperateTransitionWorkspace([], [], [], [], failure is null ? [] : [failure]);
        }

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Connections.FirstOrDefault(connection => connection.Id == connectionId));

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.ResourceEdits.FirstOrDefault(resource => resource.ResourceId == resourceId));

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Services.FirstOrDefault(service => service.Name == serviceName));
    }
}
