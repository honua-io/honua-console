using System.Collections;
using System.Text.Json;
using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

// Exercises the build metadata that the single deployable artifact serves at
// `/version.json` (Program.cs MapGet -> ConsoleBuildMetadata.Create()). This is
// the release-promotion source of truth documented in
// docs/deployment/BUILD_ARTIFACT.md, including the legacy Portal/Admin
// transition status gates. The standalone Node writer (scripts/write-build-metadata.mjs)
// has its own unit test; these assertions pin the .NET endpoint to the same
// contract so the two stay aligned.
[Collection(nameof(ConsoleBuildMetadataTests))]
[CollectionDefinition(nameof(ConsoleBuildMetadataTests), DisableParallelization = true)]
public sealed class ConsoleBuildMetadataTests : IDisposable
{
    private static readonly string[] EnvKeys =
    {
        "HONUA_CONSOLE_COMMIT_SHA",
        "HONUA_CONSOLE_REF",
        "HONUA_CONSOLE_BUILT_AT",
        "HONUA_CONSOLE_LEGACY_PORTAL_STATUS",
        "HONUA_CONSOLE_LEGACY_ADMIN_STATUS",
    };

    private readonly Dictionary<string, string?> _saved = new();

    public ConsoleBuildMetadataTests()
    {
        foreach (var key in EnvKeys)
        {
            _saved[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in _saved)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public void CreateEmitsTheVersionJsonContractKeys()
    {
        var metadata = ConsoleBuildMetadata.Create();

        Assert.Equal("honua-console", metadata["name"]);
        Assert.Contains("version", metadata.Keys);
        Assert.Contains("commit", metadata.Keys);
        Assert.Contains("shortCommit", metadata.Keys);
        Assert.Contains("ref", metadata.Keys);
        Assert.Contains("builtAt", metadata.Keys);
        Assert.Contains("legacy", metadata.Keys);
        Assert.Contains("areas", metadata.Keys);
    }

    [Fact]
    public void LegacyTransitionStatusDefaultsToActiveForPortalAndAdmin()
    {
        var legacy = Assert.IsType<Dictionary<string, string>>(ConsoleBuildMetadata.Create()["legacy"]);

        Assert.Equal("active", legacy["portal"]);
        Assert.Equal("active", legacy["admin"]);
    }

    [Fact]
    public void LegacyTransitionStatusHonorsEnvironmentGates()
    {
        Environment.SetEnvironmentVariable("HONUA_CONSOLE_LEGACY_PORTAL_STATUS", "retired");
        Environment.SetEnvironmentVariable("HONUA_CONSOLE_LEGACY_ADMIN_STATUS", "retiring");

        var legacy = Assert.IsType<Dictionary<string, string>>(ConsoleBuildMetadata.Create()["legacy"]);

        Assert.Equal("retired", legacy["portal"]);
        Assert.Equal("retiring", legacy["admin"]);
    }

    [Fact]
    public void CommitGatesPopulateFullAndTruncatedShortSha()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        Environment.SetEnvironmentVariable("HONUA_CONSOLE_COMMIT_SHA", sha);
        Environment.SetEnvironmentVariable("HONUA_CONSOLE_REF", "release/2026.06");

        var metadata = ConsoleBuildMetadata.Create();

        Assert.Equal(sha, metadata["commit"]);
        Assert.Equal("0123456789ab", metadata["shortCommit"]);
        Assert.Equal("release/2026.06", metadata["ref"]);
    }

    [Fact]
    public void AreasMatchTheNodeWriterAreaRegistry()
    {
        // scripts/write-build-metadata.mjs sources `areas` from this same file,
        // so the served endpoint and the standalone artifact writer agree.
        var registryPath = LocateRepoFile(Path.Combine("config", "console-areas.json"));
        var registryAreas = JsonSerializer.Deserialize<string[]>(File.ReadAllText(registryPath));

        var areas = ((IEnumerable)ConsoleBuildMetadata.Create()["areas"]!).Cast<string>().ToArray();

        Assert.NotNull(registryAreas);
        Assert.Equal(registryAreas, areas);
    }

    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} above {AppContext.BaseDirectory}.");
    }
}
