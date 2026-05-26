using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>The clause families the Console predicate builder compiles into a server <c>FilterPlan</c>.</summary>
public enum SavedQueryPredicateKind
{
    Attribute,
    Spatial,
    Temporal
}

/// <summary>
/// One editable predicate row in the builder. Mutable so Blazor two-way binding can drive the form;
/// the service compiles a list of these into the immutable server <see cref="FilterPlan"/>.
/// </summary>
public sealed class SavedQueryPredicateDraft
{
    /// <summary>Stable identity for Blazor list rendering (<c>@key</c>); not part of the wire contract.</summary>
    public Guid Key { get; } = Guid.NewGuid();

    public SavedQueryPredicateKind Kind { get; set; } = SavedQueryPredicateKind.Attribute;

    // Attribute / Temporal share the property field; Spatial ignores it (geometry column is implicit).
    public string Property { get; set; } = string.Empty;

    // Attribute: eq/ne/lt/le/gt/ge/like/in. Spatial: intersects/within/contains/dwithin.
    // Temporal: before/after/during.
    public string Operator { get; set; } = "eq";

    // Attribute value (number/bool/string, or comma list for "in").
    public string Value { get; set; } = string.Empty;

    // Spatial geometry as GeoJSON; distance only meaningful for the dwithin operator.
    public string GeometryGeoJson { get; set; } = string.Empty;
    public double? Distance { get; set; }
    public string DistanceUnit { get; set; } = string.Empty;

    // Temporal bounds (ISO 8601). "during" uses both; before/after use Start.
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
}

/// <summary>
/// The full working state the editor binds to: source binding, output shape, parameters, and the
/// predicate list. Mutable for form binding; the service maps it to the server-owned
/// <see cref="SavedQueryContent"/> and back.
/// </summary>
public sealed class SavedQueryDefinitionDraft
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string NaturalLanguageQuery { get; set; } = string.Empty;

    // Source binding (the Analysis Content contract does not enumerate layers, so the source is a typed
    // entry validated on preview/save against the live server).
    public int LayerId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string OutFields { get; set; } = string.Empty; // comma-separated in the UI

    // Parameters the saved-query contract exposes.
    public int? OutputSrid { get; set; }
    public string Units { get; set; } = string.Empty;
    public int? PreviewLimit { get; set; }

    public FilterPlanCombinator Combinator { get; set; } = FilterPlanCombinator.And;
    public List<SavedQueryPredicateDraft> Predicates { get; set; } = [];

    public SavedQueryDefinitionDraft Clone() => new()
    {
        Name = Name,
        Title = Title,
        NaturalLanguageQuery = NaturalLanguageQuery,
        LayerId = LayerId,
        ServiceName = ServiceName,
        OutFields = OutFields,
        OutputSrid = OutputSrid,
        Units = Units,
        PreviewLimit = PreviewLimit,
        Combinator = Combinator,
        Predicates = Predicates
            .Select(predicate => new SavedQueryPredicateDraft
            {
                Kind = predicate.Kind,
                Property = predicate.Property,
                Operator = predicate.Operator,
                Value = predicate.Value,
                GeometryGeoJson = predicate.GeometryGeoJson,
                Distance = predicate.Distance,
                DistanceUnit = predicate.DistanceUnit,
                Start = predicate.Start,
                End = predicate.End
            })
            .ToList()
    };
}

/// <summary>
/// Blocked / missing-binding surface for the saved-query editor, mirroring the Studio authoring binding
/// state. Non-null when the server cannot be reached, rejects the request, or is not configured. Never
/// carries mock query data.
/// </summary>
public sealed record SavedQueryBindingState(string State, string Contract, string Detail);

/// <summary>
/// A stable downstream artifact reference produced by the server (preview artifact or resolved data
/// source). Lets map/dashboard/report/app/workflow editors bind to the saved query's output.
/// </summary>
public sealed record SavedQueryArtifactBinding(
    string ArtifactId,
    string Role,
    string TargetKind,
    string TargetSlot,
    string? SourceVersionId);

/// <summary>
/// Identifies the saved server content item and its current immutable version. This is the reuse handle
/// other editors consume: <see cref="ItemId"/> + <see cref="CurrentVersionId"/> (+ an optional
/// <see cref="SavedQueryArtifactBinding"/> once a preview/artifact has been produced).
/// </summary>
public sealed record SavedQueryHandle(
    string ItemId,
    string Name,
    string? Title,
    string Visibility,
    int CurrentVersion,
    string CurrentVersionId,
    SavedQueryArtifactBinding? Binding = null)
{
    /// <summary>Compact reference other editors can deep-link / bind to.</summary>
    public string ReuseRef => $"{ItemId}@v{CurrentVersion}";
}

public sealed record SavedQueryPreviewRow(
    long Id,
    bool HasGeometry,
    IReadOnlyDictionary<string, string> Cells);

/// <summary>
/// A best-effort preview: a full attribute table plus a map summary. The Analysis Content preview
/// contract returns attributes and a <c>hasGeometry</c> flag but no geometry payload, so a true
/// MapLibre geometry render is not possible from this endpoint - the map panel shows a feature/geometry
/// summary and defers geometry rendering to a follow-on.
/// </summary>
public sealed record SavedQueryPreviewView(
    string PreviewArtifactId,
    int Version,
    int LayerId,
    IReadOnlyList<string> Columns,
    IReadOnlyList<SavedQueryPreviewRow> Rows,
    long? TotalCount,
    bool ExceededPreviewLimit,
    int FeatureCount,
    int GeometryCount,
    SavedQueryArtifactBinding Binding);

public sealed record SavedQuerySaveResult(SavedQueryHandle? Handle, SavedQueryBindingState? BindingState)
{
    public bool IsSuccess => BindingState is null && Handle is not null;

    public static SavedQuerySaveResult Success(SavedQueryHandle handle) => new(handle, null);

    public static SavedQuerySaveResult Blocked(SavedQueryBindingState state) => new(null, state);
}

public sealed record SavedQueryOpenResult(
    SavedQueryDefinitionDraft? Definition,
    SavedQueryHandle? Handle,
    SavedQueryBindingState? BindingState)
{
    public bool IsSuccess => BindingState is null && Definition is not null && Handle is not null;

    public static SavedQueryOpenResult Success(SavedQueryDefinitionDraft definition, SavedQueryHandle handle) =>
        new(definition, handle, null);

    public static SavedQueryOpenResult Blocked(SavedQueryBindingState state) => new(null, null, state);
}

public sealed record SavedQueryPreviewOutcome(SavedQueryPreviewView? Preview, SavedQueryBindingState? BindingState)
{
    public bool IsSuccess => BindingState is null && Preview is not null;

    public static SavedQueryPreviewOutcome Success(SavedQueryPreviewView preview) => new(preview, null);

    public static SavedQueryPreviewOutcome Blocked(SavedQueryBindingState state) => new(null, state);
}
