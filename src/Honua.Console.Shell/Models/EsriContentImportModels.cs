namespace Honua.Console.Shell.Models;

// Console-side projections for the four Esri content-import surfaces (issue #122 / #100, #101, #104, #102).
//
// The Esri-JSON parsing and the source -> target mapping are deterministic, Console-side work derived
// entirely from the uploaded JSON — that is real, not mocked (Console Patterns Charter section 11). Anything
// that needs a live server (the publish target, the #102 migration RUN engine) is gated behind an explicit
// missing-binding state via IEsriMigrationRunDataSource; these records carry only the parsed conversion
// preview, never fabricated run results.

/// <summary>
/// Per-item fidelity classification, mirroring the design-handoff <c>Fid</c> helper
/// (clean / degrade / drop / manual). Every source -> target row carries one.
/// </summary>
public enum ImportFidelity
{
    /// <summary>Converts clean — full fidelity (green).</summary>
    Clean,

    /// <summary>Degrades — usable, but something changed; the change is named in the note (yellow).</summary>
    Degrade,

    /// <summary>Dropped — can't import; the reason is named in the note (red).</summary>
    Drop,

    /// <summary>Needs review — usually a data-resource binding is required (blue).</summary>
    Manual,
}

/// <summary>The kind of Esri content an intake parsed.</summary>
public enum EsriContentKind
{
    WebMap,
    Dashboard,
    StoryMap,
}

/// <summary>
/// One parsed source -> target mapping row shared by every surface's mapping table. The
/// <see cref="SourceName"/>/<see cref="SourceType"/> describe the Esri thing; <see cref="TargetName"/>
/// the Honua thing; <see cref="Fidelity"/> + <see cref="Note"/> explain any degrade/drop/binding.
/// </summary>
public sealed record EsriMappingRow(
    string SourceName,
    string SourceType,
    string? TargetName,
    ImportFidelity Fidelity,
    string Note,
    bool Included = true,
    string? BoundResource = null);

/// <summary>An extra carried-over artifact summarized below a mapping table (basemap, bookmarks, popups).</summary>
public sealed record EsriCarryOver(string Label, bool Carried);

/// <summary>
/// The full deterministic conversion preview for one parsed Esri document. This is the
/// <c>honua.map-package.v1</c> mapping for #100, the dashboard package for #101, and the report content
/// for #104. It is computed entirely from the uploaded JSON.
/// </summary>
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

    /// <summary>Rows that convert clean (full fidelity).</summary>
    public int CleanCount => Rows.Count(r => r.Fidelity == ImportFidelity.Clean);

    /// <summary>Rows that degrade.</summary>
    public int DegradeCount => Rows.Count(r => r.Fidelity == ImportFidelity.Degrade);

    /// <summary>Rows that are dropped.</summary>
    public int DropCount => Rows.Count(r => r.Fidelity == ImportFidelity.Drop);

    /// <summary>Rows that need a manual binding/review.</summary>
    public int ManualCount => Rows.Count(r => r.Fidelity == ImportFidelity.Manual);

    /// <summary>Rows that carry into the target package (everything but dropped rows).</summary>
    public int MappedCount => Rows.Count(r => r.Fidelity != ImportFidelity.Drop);

    /// <summary>True when at least one row needs a data-resource binding before its target renders.</summary>
    public bool HasUnboundLayers => ManualCount > 0;

    /// <summary>The first unbound row, used by the inline missing-binding banner.</summary>
    public EsriMappingRow? FirstUnbound => Rows.FirstOrDefault(r => r.Fidelity == ImportFidelity.Manual);

    public static EsriConversionResult Empty(EsriContentKind kind, string targetSchema) =>
        new(kind, string.Empty, string.Empty, targetSchema, string.Empty, NoSummary, NoRows, NoCarryOver);
}

/// <summary>Outcome of a parse attempt: a conversion result on success, or an error message on bad JSON.</summary>
public sealed record EsriParseOutcome(EsriConversionResult? Result, string? Error)
{
    public bool Succeeded => Result is not null && Error is null;

    public static EsriParseOutcome Success(EsriConversionResult result) => new(result, null);

    public static EsriParseOutcome Failure(string error) => new(null, error);
}
