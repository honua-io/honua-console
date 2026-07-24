using Honua.Console.Shell.Layout;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class ConsoleLayoutTests
{
    [Theory]
    [InlineData("", "Home | Honua Console")]
    [InlineData("studio/map", "Map Builder · Studio | Honua Console")]
    [InlineData("catalog/parcels?tab=metadata", "Catalog Item | Honua Console")]
    [InlineData("operate/connections/new", "Connections · Operate | Honua Console")]
    [InlineData("operate/layers/parcels/style", "Data & Layers · Operate | Honua Console")]
    [InlineData("share/manage#links", "Manage Sharing | Honua Console")]
    public void BuildDocumentTitle_returns_useful_route_aware_title(string path, string expected)
    {
        Assert.Equal(expected, ConsoleLayout.BuildDocumentTitle(path));
    }
}
