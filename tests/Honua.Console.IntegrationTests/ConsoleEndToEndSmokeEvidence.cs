using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.IntegrationTests;

// sourceHydrated marks a real-server run so synthetic harness evidence cannot satisfy the live-server gate.
public sealed record ConsoleEndToEndSmokeEvidence
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "console-e2e-smoke-real-server";

    [JsonPropertyName("ranAt")]
    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("sourceHydrated")]
    public bool SourceHydrated { get; init; }

    [JsonPropertyName("serverImage")]
    public string ServerImage { get; init; } = string.Empty;

    [JsonPropertyName("serverBaseAddress")]
    public string ServerBaseAddress { get; init; } = string.Empty;

    [JsonPropertyName("seedProfile")]
    public IReadOnlyDictionary<string, string> SeedProfile { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("steps")]
    public IReadOnlyList<ConsoleEndToEndSmokeStep> Steps { get; init; } = [];

    [JsonPropertyName("result")]
    public string Result { get; init; } = "ok";

    public async Task WriteAsync()
    {
        var path = Environment.GetEnvironmentVariable("HONUA_CONSOLE_E2E_EVIDENCE_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                ResolveRepoRoot(),
                "smoke-evidence",
                "console-e2e-smoke.json");
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

public sealed record ConsoleEndToEndSmokeStep(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("owningLayer")] string OwningLayer,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("evidence")] string Evidence);
