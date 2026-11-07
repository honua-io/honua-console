namespace Honua.Console.Shell.Models;

// Console-side projections for the four source content-import surfaces (issue #122 / #100, #101, #104, #102).
//
// The GeoServices JSON parsing and the source -> target mapping are deterministic, Console-side work derived
// entirely from the uploaded JSON — that is real, not mocked (Console Patterns Charter section 11). Anything
// that needs a live server (the publish target, the #102 migration RUN engine) is gated behind an explicit
// missing-binding state via IEsriMigrationRunDataSource; these records carry only the parsed conversion
// preview, never fabricated run results.

public enum ImportFidelity
{
    Clean,

    Degrade,

    Drop,

    Manual,
}

public enum EsriContentKind
{
    WebMap,
    Dashboard,
    StoryMap,
    InstantApp,
    Notebook,
}

public sealed record EsriMappingRow(
    string SourceName,
    string SourceType,
    string? TargetName,
    ImportFidelity Fidelity,
    string Note,
    bool Included = true,
    string? BoundResource = null);

public sealed record EsriCarryOver(string Label, bool Carried);

public sealed record EsriConversionResult(
    EsriContentKind Kind,
    string SourceLabel,
    string SourceFile,
    string TargetSchema,
    string SuggestedName,
    IReadOnlyList<string> IntakeSummary,
    IReadOnlyList<EsriMappingRow> Rows,
    IReadOnlyList<EsriCarryOver> CarryOver)
{
    private static readonly IReadOnlyList<EsriMappingRow> NoRows = [];
    private static readonly IReadOnlyList<EsriCarryOver> NoCarryOver = [];
    private static readonly IReadOnlyList<string> NoSummary = [];

    public int CleanCount => Rows.Count(r => r.Fidelity == ImportFidelity.Clean);

    public int DegradeCount => Rows.Count(r => r.Fidelity == ImportFidelity.Degrade);

    public int DropCount => Rows.Count(r => r.Fidelity == ImportFidelity.Drop);

    public int ManualCount => Rows.Count(r => r.Fidelity == ImportFidelity.Manual);

    public int MappedCount => Rows.Count(r => r.Fidelity != ImportFidelity.Drop);

    public bool HasUnboundLayers => ManualCount > 0;

    public EsriMappingRow? FirstUnbound => Rows.FirstOrDefault(r => r.Fidelity == ImportFidelity.Manual);

    public static EsriConversionResult Empty(EsriContentKind kind, string targetSchema) =>
        new(kind, string.Empty, string.Empty, targetSchema, string.Empty, NoSummary, NoRows, NoCarryOver);
}

public sealed record EsriParseOutcome(EsriConversionResult? Result, string? Error)
{
    public bool Succeeded => Result is not null && Error is null;

    public static EsriParseOutcome Success(EsriConversionResult result) => new(result, null);

    public static EsriParseOutcome Failure(string error) => new(null, error);
}
