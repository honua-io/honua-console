using Microsoft.AspNetCore.Components;

namespace Honua.Console.Web.Runtime;

internal static class ConsoleEmbedRoutes
{
    // EmbedLayout audience routes: no Conversation drawer and no operator theme controls.
    private static readonly string[] AudiencePrefixes = ["/console/embed/", "/console/maps/published/"];

    internal static bool IsEmbed(NavigationManager navigation) => IsEmbed(navigation.Uri);

    internal static bool IsEmbed(string uri)
    {
        var path = new Uri(uri).AbsolutePath;
        return AudiencePrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
