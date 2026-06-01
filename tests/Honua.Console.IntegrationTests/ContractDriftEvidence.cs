using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Machine-readable evidence emitted by <see cref="VersionContractDriftCrossCuttingTests"/> recording the
/// pinned <c>:nightly</c> honua-server version + metadata contract the console ran against
/// (console-integration-test-plan.md §5.5). Mirroring the SDK conformance lane's pinning, this makes a drift
/// between the console's expected contract and the live server VISIBLE in the run evidence: the nightly lane
/// collects it as an artifact, so a server route rename or schema bump that the console has not yet absorbed
/// is recorded alongside the test results rather than discovered by a user. Written under
/// <c>smoke-evidence/console-contract-drift.json</c>, or to <c>HONUA_CONSOLE_CONTRACT_EVIDENCE_PATH</c>.
/// </summary>
public sealed record ContractDriftEvidence
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "console-contract-drift";

    [JsonPropertyName("ranAt")]
    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The pinned honua-server image the run targeted (HONUA_CONSOLE_SERVER_IMAGE) or the external origin.</summary>
    [JsonPropertyName("serverImage")]
    public string ServerImage { get; init; } = string.Empty;

    [JsonPropertyName("serverBaseAddress")]
    public string ServerBaseAddress { get; init; } = string.Empty;

    /// <summary>The server version string read from /api/v1/admin/version (or /capabilities).</summary>
    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; init; }

    /// <summary>The server's metadata API contract version — the drift-sensitive console↔server contract.</summary>
    [JsonPropertyName("metadataApiVersion")]
    public string? MetadataApiVersion { get; init; }

    [JsonPropertyName("metadataSchemaVersion")]
    public string? MetadataSchemaVersion { get; init; }

    /// <summary>Whether the console's expected version/capabilities routes resolved against the live server.</summary>
    [JsonPropertyName("versionRouteMounted")]
    public bool VersionRouteMounted { get; init; }

    [JsonPropertyName("capabilitiesRouteMounted")]
    public bool CapabilitiesRouteMounted { get; init; }

    /// <summary>The console operate projection's rendered version string — compared with the independent read.</summary>
    [JsonPropertyName("consoleProjectedVersion")]
    public string? ConsoleProjectedVersion { get; init; }

    public async Task WriteAsync()
    {
        var path = Environment.GetEnvironmentVariable("HONUA_CONSOLE_CONTRACT_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(ResolveRepoRoot(), "smoke-evidence", "console-contract-drift.json");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, EvidenceJsonOptions);
        await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Honua.Console.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static readonly JsonSerializerOptions EvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}
