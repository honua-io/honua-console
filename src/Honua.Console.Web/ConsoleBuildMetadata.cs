using System.Reflection;
using Honua.Console.Shell.Models;

namespace Honua.Console.Web;

public static class ConsoleBuildMetadata
{
    private static readonly string StartedAt = DateTimeOffset.UtcNow.ToString("O");

    public static IReadOnlyDictionary<string, object?> Create()
    {
        var sha = Environment.GetEnvironmentVariable("HONUA_CONSOLE_COMMIT_SHA") ?? "unknown";

        return new Dictionary<string, object?>
        {
            ["name"] = "honua-console",
            ["version"] = Version,
            ["commit"] = sha,
            ["shortCommit"] = sha.Length > 12 ? sha[..12] : sha,
            ["ref"] = Environment.GetEnvironmentVariable("HONUA_CONSOLE_REF") ?? "unknown",
            ["builtAt"] = Environment.GetEnvironmentVariable("HONUA_CONSOLE_BUILT_AT") ?? StartedAt,
            ["legacy"] = new Dictionary<string, string>
            {
                ["portal"] = Environment.GetEnvironmentVariable("HONUA_CONSOLE_LEGACY_PORTAL_STATUS") ?? "active",
                ["admin"] = Environment.GetEnvironmentVariable("HONUA_CONSOLE_LEGACY_ADMIN_STATUS") ?? "active"
            },
            ["areas"] = ConsoleRouteMap.Areas.Select(area => area.Id).ToArray()
        };
    }

    private static string Version =>
        typeof(ConsoleBuildMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            .Split('+', 2)[0] ?? "unknown";
}
