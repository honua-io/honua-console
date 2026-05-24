using Honua.Console.Shell.Models;

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
}
