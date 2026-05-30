namespace Honua.Console.Shell.Models;

// Editor- and data-source-facing projections for the Studio spatial query builder (/studio/query,
// honua-console#52).
//
// These model the spatial query a Console operator authors before saving it as a reusable content
// version: the source binding (service/layer), the predicate builder (comparison/spatial/temporal
// clauses combined by a logical combinator), the attribute projection, the generated SQL/filter readout,
// the parameter editor, and the map/table preview pulled back from honua-server. They are deliberately a
// thin Console projection over the server-owned saved-query content/artifacts contract
// (honua-server#1182, AnalysisContentKind.SavedQuery); they are NOT a Console-owned wire schema and must
// not be duplicated onto the server/SDK contract.
//
// Per the Console Patterns Charter section 11 there is no standing in-memory query client in the merged
// result: when no honua-server base address is configured the unsupported data source returns an explicit
// missing-binding capability state instead of fabricating query packages.

/// <summary>
/// A saved spatial query loaded into the builder. A draft authored against the server-owned saved-query
/// content; the source binding, predicate clauses, projection, parameters, generated SQL/filter readout,
/// and the map/table preview are projected from this state before save/publish.
/// </summary>
public sealed class StudioQueryEditor
{
    /// <summary>Server-assigned content item id, or null for a not-yet-saved draft.</summary>
    public string? QueryId { get; set; }

    /// <summary>Current immutable version of the open draft.</summary>
    public int Version { get; set; }

    /// <summary>Optimistic-concurrency tag (server content hash) carried from the loaded version.</summary>
    public string? ETag { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Original natural-language query text carried onto the server saved-query content.</summary>
    public string NaturalLanguageQuery { get; set; } = string.Empty;

    /// <summary>Source service name binding (server SavedQueryContent.ServiceName).</summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>Target layer id the query runs against (server SavedQueryContent.LayerId).</summary>
    public int LayerId { get; set; }

    /// <summary>Top-level logical combinator joining the predicate clauses.</summary>
    public string Combinator { get; set; } = StudioQueryCombinators.And;

    /// <summary>The predicate clauses (comparison/spatial/temporal) of the filter plan.</summary>
    public List<StudioQueryPredicateEditor> Predicates { get; } = [];

    /// <summary>Attribute field projection. Empty means all fields.</summary>
    public List<string> OutFields { get; } = [];

    /// <summary>Run-time parameters surfaced as the parameter editor list.</summary>
    public List<StudioQueryParameterEditor> Parameters { get; } = [];

    /// <summary>Desired output spatial reference id, when overriding the source SRID.</summary>
    public int? OutputSrid { get; set; }

    /// <summary>Default preview row limit for this saved query.</summary>
    public int PreviewLimit { get; set; } = 25;

    /// <summary>Preferred output format for downstream consumers (geojson/esrijson/csv).</summary>
    public string OutputFormat { get; set; } = StudioQueryOutputFormats.All[0];

    /// <summary>The most recent map/table preview pulled from the live server, if any.</summary>
    public StudioQueryPreview? Preview { get; set; }

    public bool IsExistingQuery => !string.IsNullOrWhiteSpace(QueryId);
}

/// <summary>
/// A single predicate in the query builder. The <see cref="Kind"/> selects which of comparison/spatial/
/// temporal fields are authored; the data source lowers it into the matching server filter clause.
/// </summary>
public sealed class StudioQueryPredicateEditor
{
    public string Kind { get; set; } = StudioQueryPredicateKinds.Comparison;

    /// <summary>Field/property name (comparison + temporal clauses).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Operator: comparison, spatial (intersects/within/...), or temporal (before/after/during).</summary>
    public string Operator { get; set; } = StudioQueryComparisonOperators.All[0];

    /// <summary>Literal value for comparison clauses, or distance for a spatial dwithin clause.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>GeoJSON geometry literal for spatial clauses.</summary>
    public string Geometry { get; set; } = string.Empty;

    /// <summary>Distance unit for a spatial dwithin clause (meters/kilometers/...).</summary>
    public string DistanceUnit { get; set; } = "meters";

    /// <summary>Start of a temporal range (ISO 8601).</summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>End of a temporal range (ISO 8601).</summary>
    public string End { get; set; } = string.Empty;
}

public sealed class StudioQueryParameterEditor
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The map/table preview pulled back from the live server preview route, surfaced on the builder preview
/// panel. Carries the resolved preview feature rows, the result count, and the downstream binding so the
/// operator can reuse the saved query as input to a map/dashboard/report/app/workflow editor (AC#3).
/// </summary>
public sealed record StudioQueryPreview(
    string PreviewArtifactId,
    int LayerId,
    long? TotalCount,
    bool ExceededPreviewLimit,
    IReadOnlyList<StudioQueryPreviewFeatureView> Features,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> DownstreamTargets)
{
    public int FeatureCount => Features.Count;

    public bool HasGeometry => Features.Any(feature => feature.HasGeometry);
}

/// <summary>A single preview feature row for the table view.</summary>
public sealed record StudioQueryPreviewFeatureView(
    long Id,
    bool HasGeometry,
    IReadOnlyDictionary<string, string> Attributes);

public static class StudioQueryCombinators
{
    public const string And = "and";
    public const string Or = "or";

    public static readonly IReadOnlyList<string> All = [And, Or];
}

public static class StudioQueryPredicateKinds
{
    public const string Comparison = "comparison";
    public const string Spatial = "spatial";
    public const string Temporal = "temporal";

    public static readonly IReadOnlyList<string> All = [Comparison, Spatial, Temporal];
}

public static class StudioQueryComparisonOperators
{
    public static readonly IReadOnlyList<string> All =
    [
        "=",
        "!=",
        ">",
        ">=",
        "<",
        "<=",
        "like",
        "in",
    ];
}

public static class StudioQuerySpatialOperators
{
    public static readonly IReadOnlyList<string> All =
    [
        "intersects",
        "within",
        "contains",
        "dwithin",
    ];
}

public static class StudioQueryTemporalOperators
{
    public static readonly IReadOnlyList<string> All =
    [
        "before",
        "after",
        "during",
    ];
}

public static class StudioQueryOutputFormats
{
    public static readonly IReadOnlyList<string> All =
    [
        "geojson",
        "esrijson",
        "csv",
    ];
}

// --- Data-source-facing projections (immutable). ---

/// <summary>
/// A binding/permission/empty surface for the query builder, mirroring the form-builder and Operate
/// capability-state pattern so missing bindings, missing permissions, and unsupported contracts render
/// consistently across Studio editors.
/// </summary>
public sealed record StudioQueryCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// The query builder workspace: the server's saved query packages plus any binding/permission capability
/// states. An empty package list with no capability states is a real (bound) empty workspace; a
/// missing-binding capability state means no server is configured and the surface is blocked.
/// </summary>
public sealed record StudioQueryWorkspace(
    IReadOnlyList<StudioQueryPackageListItem> Packages,
    IReadOnlyList<StudioQueryCapabilityState> CapabilityStates);

/// <summary>One saved query package as listed in the builder workspace.</summary>
public sealed record StudioQueryPackageListItem(
    string QueryId,
    string Title,
    string SourceBinding,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);

/// <summary>The result of loading (or starting) a query draft into the builder.</summary>
public sealed record StudioQueryEditorLoad(
    StudioQueryEditor? Query,
    IReadOnlyList<StudioQueryCapabilityState> CapabilityStates)
{
    public bool HasEditor => Query is not null;
}

/// <summary>Outcome of a mutating query lifecycle command (save, preview).</summary>
public sealed record StudioQueryCommandResult(
    bool Succeeded,
    string Message,
    StudioQueryEditor? Query = null,
    StudioQueryCapabilityState? Issue = null);
