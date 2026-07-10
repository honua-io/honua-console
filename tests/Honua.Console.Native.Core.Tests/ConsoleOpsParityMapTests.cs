using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Guards the route-level Console/operator seat-parity contract tracked by #291.
/// DTO and response-shape drift belongs to the individual contract tests, not this map.
/// </summary>
public sealed class ConsoleOpsParityMapTests
{
    private const string ParityMapRelativePath = "contracts/honua-server/ops-parity-map.yaml";
    private const string SourceMetadataRelativePath = "contracts/honua-server/ops-parity-map.source.json";

    private static readonly Regex RouteLineRegex = new(
        "^\\s{2}\\\"(?<route>[^\\\"]+)\\\"\\s*:\\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ConsoleOperateRouteConstants_AreCoveredByVendoredServerParityMap()
    {
        var repoRoot = ResolveRepositoryRoot();
        var parityMapPath = Path.Combine(repoRoot, ParityMapRelativePath);
        Assert.True(
            File.Exists(parityMapPath),
            $"The vendored honua-server ops parity map is missing at {ParityMapRelativePath}. "
            + "Run scripts/sync-ops-parity-map.sh --update <server-commit>.");

        var serverRoutes = LoadParityMapRoutes(parityMapPath);
        var consoleRoutes = LoadConsoleRouteConstants();
        var missingRoutes = consoleRoutes
            .Where(route => !serverRoutes.Contains(route))
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        var failure = string.Join(
            Environment.NewLine,
            missingRoutes.Select(route =>
                $"Route {route} exists in Console contracts but not in the vendored parity map. "
                + "Add it to honua-server's ops parity map with an MCP mapping or a human-only justification, "
                + "then re-vendor it in the PR that added it by running "
                + "scripts/sync-ops-parity-map.sh --update <server-commit>."));

        Assert.True(missingRoutes.Length == 0, failure);
    }

    [Fact]
    public void VendoredParityMap_RecordsImmutableServerSource()
    {
        var metadataPath = Path.Combine(ResolveRepositoryRoot(), SourceMetadataRelativePath);
        Assert.True(
            File.Exists(metadataPath),
            $"The vendored parity map source metadata is missing at {SourceMetadataRelativePath}.");

        using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var root = document.RootElement;

        Assert.Equal("honua-io/honua-server", root.GetProperty("repository").GetString());
        Assert.Equal(
            "tests/dotnet/Honua.Ai.Tests/ConformanceSchemas/geospatial-mcp/ops-parity-map.yaml",
            root.GetProperty("path").GetString());

        var commit = root.GetProperty("commit").GetString();
        Assert.Matches("^[0-9a-f]{40}$", commit ?? string.Empty);
    }

    private static HashSet<string> LoadConsoleRouteConstants()
    {
        var assembly = typeof(OpsHealthRoutes).Assembly;
        var routes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type
                         .GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(field => field.IsLiteral && field.FieldType == typeof(string)))
            {
                var path = (string?)field.GetRawConstantValue();
                if (path is null || !IsOpsParityPath(path))
                {
                    continue;
                }

                var parityAttribute = field.CustomAttributes.SingleOrDefault(attribute =>
                    string.Equals(
                        attribute.AttributeType.FullName,
                        "Honua.Console.Contracts.OpsParityRouteAttribute",
                        StringComparison.Ordinal));

                Assert.True(
                    parityAttribute is not null,
                    $"Route constant {type.FullName}.{field.Name} must declare [OpsParityRoute(\"METHOD\")] "
                    + "so the seat-parity test can compare the exact HTTP route.");

                var method = parityAttribute!.ConstructorArguments.Single().Value as string;
                Assert.False(string.IsNullOrWhiteSpace(method));
                routes.Add($"{method!.ToUpperInvariant()} /{path.TrimStart('/')}");
            }
        }

        return routes;
    }

    private static bool IsOpsParityPath(string path)
    {
        var normalized = $"/{path.TrimStart('/')}";
        return normalized.StartsWith("/api/v1/operate/", StringComparison.Ordinal)
            || normalized.StartsWith("/api/v1/admin/observability/ops-health", StringComparison.Ordinal)
            || normalized.StartsWith("/api/v1/admin/observability/findings", StringComparison.Ordinal)
            || normalized.StartsWith("/api/v1/admin/deploy/", StringComparison.Ordinal)
            || normalized.StartsWith("/api/v1/admin/proposals", StringComparison.Ordinal)
            || string.Equals(normalized, "/api/v1/admin/platform-release/converge", StringComparison.Ordinal);
    }

    private static HashSet<string> LoadParityMapRoutes(string path)
    {
        var routes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            var match = RouteLineRegex.Match(line);
            if (match.Success)
            {
                Assert.True(routes.Add(match.Groups["route"].Value), $"Duplicate route in vendored parity map: {line.Trim()}");
            }
        }

        Assert.NotEmpty(routes);
        return routes;
    }

    private static string ResolveRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Console.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Honua Console repository root.");
    }
}
