using Microsoft.AspNetCore.Components;

namespace Honua.Console.Web.Runtime;

internal static class ConsoleRoutes
{
    internal static bool IsEmbed(NavigationManager navigation) =>
        new Uri(navigation.Uri).AbsolutePath.StartsWith("/console/embed/", StringComparison.OrdinalIgnoreCase);
}
