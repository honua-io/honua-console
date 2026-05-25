using System.Text.Json;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleRouteMapTests
{
    [Fact]
    public void SharedRouteMapKeepsBuilderAndOperatorAreasDistinct()
    {
        var areas = ConsoleRouteMap.Areas;

        Assert.Contains(areas, area => area.Id == "studio" && area.WorkflowBoundary == "Builder");
        Assert.Contains(areas, area => area.Id == "catalog" && area.WorkflowBoundary == "Builder");
        Assert.Contains(areas, area => area.Id == "operate" && area.WorkflowBoundary == "Operator");
        Assert.Contains(areas, area => area.Id == "share" && area.WorkflowBoundary == "Builder");
        Assert.Equal(4, areas.Select(area => area.Path).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void StudioShellOwnsSmokeAndLifecycleRouteAliases()
    {
        var routes = typeof(StudioPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/studio", routes);
        Assert.Contains("/studio/proof", routes);
        Assert.Contains("/studio/drafts", routes);
        Assert.Contains("/studio/apps/{ItemId}/preview", routes);
    }

    [Fact]
    public void SharedRouteMapMatchesBuildMetadataAreaRegistry()
    {
        var registryPath = Path.Combine(AppContext.BaseDirectory, "console-areas.json");
        var registryAreas = JsonSerializer.Deserialize<string[]>(File.ReadAllText(registryPath));

        Assert.NotNull(registryAreas);
        Assert.Equal(registryAreas, ConsoleRouteMap.Areas.Select(area => area.Id).ToArray());
    }

    [Theory]
    [InlineData("operate")]
    [InlineData("operate/connections")]
    [InlineData("/operate/resources/table/ns/name?tab=validation")]
    [InlineData("operate/settings#cors")]
    public void OperateRoutesAreRecognizedForRouteLocalNavigation(string relativePath)
    {
        Assert.True(ConsoleRouteMap.IsOperateRoute(relativePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("studio")]
    [InlineData("catalog")]
    [InlineData("share")]
    [InlineData("operate-preview")]
    [InlineData("environments")]
    public void BuilderAndNativeHostRoutesDoNotReceiveOperateNavigation(string relativePath)
    {
        Assert.False(ConsoleRouteMap.IsOperateRoute(relativePath));
    }
}
