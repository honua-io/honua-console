using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateServicesPageTests
{
    [Fact]
    public void ServicesPage_WithServices_RendersListAndEscapedSettingsLinks()
    {
        using var context = new BunitContext();
        OperateServiceDetail service = new("roads/public", "Public roads", "WMS, WFS", "Ready", "", [], [], []);
        context.Services.AddSingleton<IOperateTransitionDataSource>(
            new FixedServicesDataSource(service));

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Services", page.Find("h1").TextContent);
        Assert.Equal("Public roads", page.Find("a[href='/operate/services/roads%2Fpublic/settings']").TextContent);
        Assert.Equal(["Public roads", "WMS, WFS", "0", "Ready"], page.FindAll("tbody td").Select(cell => cell.TextContent));
        Assert.Empty(page.FindAll("[data-resource-tree], [data-connection-notice]"));
    }

    [Fact]
    public void ServicesPage_WithoutEnvironment_ShowsConnectionNoticeInsteadOfEmptyList()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new UnsupportedOperateTransitionDataSource());

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Connect environment", page.Find("[data-connection-notice] a").TextContent);
        Assert.Empty(page.FindAll("table, .console-state-empty"));
    }

    [Theory]
    [InlineData("Connections", 0)]
    [InlineData("Layers", 0)]
    [InlineData("Layers", 1)]
    public void ServicesPage_IncompleteLayerRead_DoesNotClaimATotal(string surface, int visibleLayers)
    {
        using var context = new BunitContext();
        var layers = Enumerable.Range(0, visibleLayers)
            .Select(index => new OperateServiceLayerProjection(index, "Roads", "LineString", "roads", "Roads")).ToArray();
        OperateServiceDetail service = new("roads", "Roads", "WFS", "Ready", "", layers, [], []);
        var states = new[] { new OperateCapabilityState(surface, "Forbidden", "admin", "Layer enumeration is incomplete.") };
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() =>
            Task.FromResult(new OperateServicesView([service], states))));

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Unknown", page.FindAll("tbody td")[2].TextContent);
        Assert.Single(page.FindAll("[data-connection-notice]"));
    }

    [Fact]
    public void ServicesPage_WhileLoading_DoesNotClaimTheListIsEmpty()
    {
        using var context = new BunitContext();
        var response = new TaskCompletionSource<OperateServicesView>();
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() => response.Task));

        var page = context.Render<OperateServicesPage>();

        Assert.Equal("Reading services…", page.Find("[role='status']").TextContent);
        Assert.Empty(page.FindAll(".console-state-empty"));
        response.SetResult(new OperateServicesView([], []));
        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("[role='status']"));
            Assert.Contains("services", page.Find(".console-state-empty").TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void ServicesPage_AfterReadFailure_RetriesWithoutShowingExceptionDetails()
    {
        using var context = new BunitContext();
        var requests = 0;
        context.Services.AddSingleton<IOperateTransitionDataSource>(new ServicesDataSource(() =>
            ++requests == 1
                ? Task.FromException<OperateServicesView>(new HttpRequestException("private diagnostic"))
                : Task.FromResult(new OperateServicesView([], []))));
        var page = context.Render<OperateServicesPage>();

        Assert.Contains("Could not read services", page.Find("[data-connection-notice]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("private diagnostic", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".console-state-empty"));
        page.Find("[data-connection-notice] button").Click();

        Assert.Equal(2, requests);
        Assert.Empty(page.FindAll("[data-connection-notice]"));
        Assert.Single(page.FindAll(".console-state-empty"));
    }

    private sealed class ServicesDataSource(Func<Task<OperateServicesView>> load) : IOperateTransitionDataSource
    {
        public Task<OperateServicesView> GetServicesViewAsync(CancellationToken cancellationToken = default) => load();

        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string datasetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
