using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.IntegrationTests;

// Server route and schema identity is stored with the run so a rename is caught with the results.
public sealed record ContractDriftEvidence
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "console-contract-drift";

    [JsonPropertyName("ranAt")]
    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("serverImage")]
    public string ServerImage { get; init; } = string.Empty;

    [JsonPropertyName("serverBaseAddress")]
    public string ServerBaseAddress { get; init; } = string.Empty;

    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; init; }

    [JsonPropertyName("metadataApiVersion")]
    public string? MetadataApiVersion { get; init; }

    [JsonPropertyName("metadataSchemaVersion")]
    public string? MetadataSchemaVersion { get; init; }

    [JsonPropertyName("versionRouteMounted")]
    public bool VersionRouteMounted { get; init; }

    [JsonPropertyName("capabilitiesRouteMounted")]
    public bool CapabilitiesRouteMounted { get; init; }

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
