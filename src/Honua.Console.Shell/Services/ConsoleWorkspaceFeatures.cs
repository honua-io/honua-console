using Microsoft.Extensions.Configuration;

namespace Honua.Console.Shell.Services;

internal static class ConsoleWorkspaceFeatures
{
    internal static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>("Honua:Agent:WorkspaceApiEnabled");
}
