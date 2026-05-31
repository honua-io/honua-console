using System.Text.Json;
using System.Text.Json.Serialization;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Machine-readable evidence emitted by <see cref="ConsoleEndToEndSmokeTests"/> when the real-server smoke
/// runs. Records the honua-server image/origin, the seed profile, and <c>sourceHydrated: true</c> so the
/// <c>honua-console#9</c> gate can distinguish a real-server run from the in-memory <c>smoke/parity</c>
/// harness (which records <c>sourceHydrated: false</c>). Written under
/// <c>smoke-evidence/console-e2e-smoke.json</c> next to the existing parity evidence, or to
/// <c>HONUA_CONSOLE_E2E_EVIDENCE_PATH</c> when set, so release-promotion tooling can collect it as an
/// artifact (issue #59 acceptance criteria).
/// </summary>
public sealed record ConsoleEndToEndSmokeEvidence
{
    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "console-e2e-smoke-real-server";

    [JsonPropertyName("ranAt")]
    public DateTimeOffset RanAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True only when every step ran against a live honua-server (never in-memory adapters).</summary>
    [JsonPropertyName("sourceHydrated")]
    public bool SourceHydrated { get; init; }

    /// <summary>The honua-server image (Testcontainers) or external origin the chain ran against.</summary>
    [JsonPropertyName("serverImage")]
    public string ServerImage { get; init; } = string.Empty;

    [JsonPropertyName("serverBaseAddress")]
    public string ServerBaseAddress { get; init; } = string.Empty;

    /// <summary>The seed profile applied before the chain (the fixture slugs/names this run created).</summary>
    [JsonPropertyName("seedProfile")]
    public IReadOnlyDictionary<string, string> SeedProfile { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Each cross-surface step that ran against the live server, in order.</summary>
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

    /// <summary>
    /// Resolves the repository root by walking up from the test assembly output directory until the
    /// <c>Honua.Console.slnx</c> marker is found, mirroring the <c>smoke/parity</c> harness which writes
    /// <c>smoke-evidence/console-parity.json</c> relative to the repo root. Falls back to
    /// <see cref="AppContext.BaseDirectory"/> if no marker is found so evidence is still emitted.
    /// </summary>
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
