using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using static Honua.Console.IntegrationTests.ConsolePublicationRenderFixture;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationLayerRenderTests
{
    [Fact]
    public void RouterNavigationPublishesLayerAncestryBeforeTheLayoutReceivesLocationChanged()
    {
        var source = new PublicationNavigationSource
        {
            RefuseLayerReads = true,
            ServiceSummaries = [new()
            { ServiceName = "parcels", FolderId = "public", FolderName = "Public", Title = "Parcels", LayerCount = 2 }]
        };
        using var context = PublicationLayoutContext(source);
        var first = new OperateServiceLayerProjection(7, "Bridges", "LineString", "first-layer", "bridges");
        var second = new OperateServiceLayerProjection(8, "Roads", "LineString", "second-layer", "roads");
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(
            new OperateServiceDetail("parcels", "Parcels", "FeatureServer", "Running", "server", [first, second], [], [])));
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/layers/first-layer");
        var router = context.Render<Router>(parameters => parameters
            .Add(component => component.AppAssembly, typeof(Honua.Console.Shell.Pages.OperateLayerDetailPage).Assembly)
            .Add(component => component.Found, route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddComponentParameter(1, nameof(RouteView.RouteData), route);
                builder.AddComponentParameter(2, nameof(RouteView.DefaultLayout), typeof(ConsoleLayout));
                builder.CloseComponent();
            }));
        router.WaitForAssertion(() => Assert.Contains("Public", router.Find("nav[aria-label='Breadcrumb']").TextContent));
        navigation.NavigateTo("/operate/layers/second-layer");
        router.WaitForAssertion(() => Assert.Equal("Roads", router.Find("h1").TextContent));
        router.WaitForAssertion(() => Assert.Contains("Public", router.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.Contains("Parcels", router.Find("nav[aria-label='Breadcrumb']").TextContent);
        Assert.Equal(0, source.ChildReads);
    }

    [Fact]
    public void ColdReloadUsesTheLoadedLayerExposureWhenTheDefaultServiceHasNoLayers()
    {
        var source = new PublicationNavigationSource
        {
            RefuseLayerReads = true,
            ServiceSummaries = [new()
            { ServiceName = "z-parcels", FolderId = "public", FolderName = "Public", Title = "Parcels", LayerCount = 1 }]
        };
        using var context = PublicationLayoutContext(source);
        var layer = new OperateServiceLayerProjection(7, "Bridges", "LineString", "connection-one-layer-7", "bridges");
        var service = new OperateServiceDetail("z-parcels", "Parcels", "FeatureServer", "Running", "honua-server", [layer], [], []);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource([service]));
        context.Services.AddSingleton<IConsoleLayerFieldsOperation>(new UnsupportedConsoleLayerFieldsOperation());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = context.Render<ConsoleLayout>(parameters => parameters.Add(component => component.Body, builder =>
        {
            builder.OpenComponent<Honua.Console.Shell.Pages.OperateLayerDetailPage>(0);
            builder.AddComponentParameter(1, "ResourceId", "connection-one-layer-7");
            builder.CloseComponent();
        }));
        layout.WaitForAssertion(() => Assert.Equal("Bridges", layout.Find("h1").TextContent));
        layout.WaitForAssertion(() => Assert.Contains("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.Contains("Parcels", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("server")]
    [InlineData("account")]
    public async Task ColdLayerNavigationRejectsDelayedPageExposureAfterEnvironmentChanges(string field)
    {
        var pending = new TaskCompletionSource<OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PublicationNavigationSource
        {
            PendingLayers = pending,
            ServiceSummaries = [new() { ServiceName = "parcels", FolderId = "public", FolderName = "Public", LayerCount = 1 }]
        };
        using var context = PublicationLayoutContext(source);
        var original = new ConsoleEnvironmentProfile { Id = "first", ServerBaseUri = new Uri("https://first.example.test") };
        var profiles = new InMemoryConsoleEnvironmentProfileStore([original]);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/operate/layers/connection-one-layer-7");
        var layout = RenderLayerLayout(context);
        layout.WaitForAssertion(() => Assert.Equal(1, source.LayerPageReads));
        var previous = source.LayerResult;
        source.PendingLayers = null;
        source.LayerResult = OperateSectionResult<IReadOnlyList<ConsolePublicationLayer>>.Denied(OperateSectionStatus.Forbidden, "Layers denied.");
        var changed = ChangedProfile(original, field);
        await layout.InvokeAsync(async () =>
        {
            await profiles.UpsertProfileAsync(changed);
            await profiles.ActivateProfileAsync(changed.Id);
        });
        pending.SetResult(previous);
        await layout.InvokeAsync(() => Task.CompletedTask);
        layout.WaitForAssertion(() => Assert.DoesNotContain("Public", layout.Find("nav[aria-label='Breadcrumb']").TextContent));
        Assert.DoesNotContain("parcels", layout.Find("nav[aria-label='Breadcrumb']").TextContent);
    }

}
