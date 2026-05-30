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

    /// <summary>
    /// Natural-language goal carried onto the server analysis intent (honua-server AnalysisIntent.Goal).
    /// Defaults to the title when blank so the server-owned intent is never empty.
    /// </summary>
    public string Goal { get; set; } = string.Empty;

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

    /// <summary>
    /// The execution job submitted from this plan, if any. Carries the job id/status and the resolved
    /// result artifact so the result-artifact panel can render after submit (AC#1/AC#3).
    /// </summary>
    public StudioAnalysisJobView? SubmittedJob { get; set; }

    /// <summary>
    /// The real server-owned analysis plan steps for this version, when loaded from honua-server. Drives
    /// the DAG/pipeline view from the server's compiled plan rather than a Console reconstruction (AC#2).
    /// </summary>
    public IReadOnlyList<StudioAnalysisPipelineNode> ServerPipeline { get; set; } = [];

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

/// <summary>
/// The execution job a submitted analysis plan produced, surfaced on the result panel. Status comes from
/// the server execution engine (honua-server#681/#721/#724) job lifecycle; <see cref="Failure"/> is set
/// only when the server classified a terminal failure.
/// </summary>
public sealed record StudioAnalysisJobView(
    string JobId,
    string Status,
    int Version,
    StudioAnalysisArtifactView? Artifact = null,
    StudioAnalysisJobFailureView? Failure = null)
{
    public bool HasArtifact => Artifact is not null;

    public bool HasFailure => Failure is not null;
}

/// <summary>
/// A resolved result artifact for the artifact panel. <see cref="DownstreamTargets"/> are the content
/// families this artifact can become (content item, layer, report, dashboard, workflow input) per AC#3,
/// projected from the server artifact binding contract.
/// </summary>
public sealed record StudioAnalysisArtifactView(
    string ArtifactId,
    string Kind,
    string Label,
    string? Uri,
    string? ContentType,
    long? ByteSize,
    string RetentionState,
    IReadOnlyList<string> DownstreamTargets);

/// <summary>A safe terminal-failure classification surfaced on the result panel.</summary>
public sealed record StudioAnalysisJobFailureView(
    string Classification,
    string Message);

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
