namespace Honua.Console.Shell.Models;

// Editor-facing projections for the Studio spatial analysis builder (/studio/analysis, honua-console#53).
//
// These model the plan card a Console operator authors before submitting an analysis job: the method,
// inputs, parameters, output schema, and compute profile, plus the runtime/cost estimate and the
// DAG/pipeline view of a multi-step plan. They are deliberately a thin Console projection over the
// server-owned analysis content/artifacts contract (honua-server#1182) and the closed execution engine
// (honua-server#681/#721/#724); they are NOT a Console-owned wire schema and must not be duplicated onto
// the server/SDK contract. The data-source-facing records mirror the form-builder capability-state
// pattern so missing bindings, missing permissions, and unsupported contracts render consistently.

/// <summary>
/// A spatial analysis plan loaded into the builder. A draft authored against the server-owned
/// analysis package; the plan card, parameters, output schema, and compute estimate are projected
/// from this state before preview/submit.
/// </summary>
public sealed class StudioAnalysisPlanEditor
{
    /// <summary>Server-assigned analysis package id, or null for a not-yet-saved draft.</summary>
    public string? AnalysisId { get; set; }

    /// <summary>Current version of the open draft.</summary>
    public int Version { get; set; }

    /// <summary>Lifecycle status of the open version (draft/published).</summary>
    public string Status { get; set; } = HonuaAnalysisStatuses.Draft;

    /// <summary>Optimistic-concurrency tag carried from the loaded version, echoed on save.</summary>
    public string? ETag { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>The analysis method (e.g. buffer, hotspot, overlay), drawn from <see cref="StudioAnalysisMethods.All"/>.</summary>
    public string Method { get; set; } = StudioAnalysisMethods.All[0];

    /// <summary>Input bindings (services, layers, or content) feeding the plan.</summary>
    public List<StudioAnalysisInputEditor> Inputs { get; } = [];

    /// <summary>Method parameters (name/value pairs surfaced as the plan card parameter list).</summary>
    public List<StudioAnalysisParameterEditor> Parameters { get; } = [];

    /// <summary>The output schema fields the analysis emits.</summary>
    public List<StudioAnalysisOutputFieldEditor> OutputSchema { get; } = [];

    /// <summary>Compute profile selecting the worker class the execution engine runs the job on.</summary>
    public string ComputeProfile { get; set; } = StudioAnalysisComputeProfiles.All[0];

    /// <summary>The published content family the result artifact becomes (content/layer/report/workflow input).</summary>
    public string OutputContentType { get; set; } = StudioAnalysisOutputContentTypes.All[0];

    /// <summary>The most recent server compute estimate for the saved plan, if one has been requested.</summary>
    public StudioAnalysisComputeEstimate? Estimate { get; set; }

    public bool IsExistingPackage => !string.IsNullOrWhiteSpace(AnalysisId);

    public bool IsPublished =>
        string.Equals(Status, HonuaAnalysisStatuses.Published, StringComparison.OrdinalIgnoreCase);
}

public sealed class StudioAnalysisInputEditor
{
    public string Role { get; set; } = string.Empty;

    public string ServiceId { get; set; } = string.Empty;

    public int LayerId { get; set; }
}

public sealed class StudioAnalysisParameterEditor
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class StudioAnalysisOutputFieldEditor
{
    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = StudioAnalysisFieldTypes.All[0];
}

/// <summary>The runtime/cost estimate surfaced on the plan card before submit.</summary>
public sealed record StudioAnalysisComputeEstimate(
    double EstimatedRuntimeSeconds,
    long EstimatedInputFeatures,
    string ComputeProfile,
    string? CostNote = null);

/// <summary>A single node in the analysis DAG/pipeline view of a multi-step plan.</summary>
public sealed record StudioAnalysisPipelineNode(
    string NodeId,
    string Label,
    string Method,
    IReadOnlyList<string> DependsOn);

public static class HonuaAnalysisStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
}

public static class StudioAnalysisMethods
{
    public static readonly IReadOnlyList<string> All =
    [
        "buffer",
        "overlay",
        "hotspot",
        "interpolation",
        "proximity",
        "aggregation",
        "suitability",
    ];
}

public static class StudioAnalysisComputeProfiles
{
    public static readonly IReadOnlyList<string> All =
    [
        "standard",
        "high-memory",
        "gpu",
    ];
}

public static class StudioAnalysisFieldTypes
{
    public static readonly IReadOnlyList<string> All =
    [
        "string",
        "integer",
        "double",
        "boolean",
        "date",
        "geometry",
    ];
}

public static class StudioAnalysisOutputContentTypes
{
    public static readonly IReadOnlyList<string> All =
    [
        "content",
        "layer",
        "report",
        "dashboard",
        "workflow",
    ];
}

// --- Data-source-facing projections (immutable). ---

public sealed record StudioAnalysisWorkspace(
    IReadOnlyList<StudioAnalysisPlanListItem> Plans,
    IReadOnlyList<StudioAnalysisCapabilityState> CapabilityStates);

public sealed record StudioAnalysisPlanListItem(
    string AnalysisId,
    string Title,
    string Method,
    string ComputeProfile,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A binding/permission/empty surface for the analysis builder, mirroring the Operate capability-state
/// pattern so missing bindings, missing permissions, and unsupported contracts render consistently.
/// </summary>
public sealed record StudioAnalysisCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

public sealed record StudioAnalysisEditorLoad(
    StudioAnalysisPlanEditor? Plan,
    IReadOnlyList<StudioAnalysisCapabilityState> CapabilityStates)
{
    public bool HasEditor => Plan is not null;
}

/// <summary>Outcome of a mutating analysis lifecycle command (save, estimate, preview, submit).</summary>
public sealed record StudioAnalysisCommandResult(
    bool Succeeded,
    string Message,
    StudioAnalysisPlanEditor? Plan = null,
    StudioAnalysisCapabilityState? Issue = null);
