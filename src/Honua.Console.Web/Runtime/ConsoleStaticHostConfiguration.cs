using Microsoft.Extensions.Configuration;

namespace Honua.Console.Web.Runtime;

internal static class ConsoleStaticHostConfiguration
{
    internal static void EnableBundledStudio(IConfigurationBuilder configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Studio:EmbedJsStudio"] = "true"
        });
    }
}
