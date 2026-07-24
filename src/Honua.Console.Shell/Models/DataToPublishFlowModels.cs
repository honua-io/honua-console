namespace Honua.Console.Shell.Models;

/// <summary>
/// The shared flow object for the unified resource-first "data → publish" journey (redesign §3.1). One
/// flow object is mutated by whichever driver fills it — the manual wizard today, the AI driver later
/// (the <see cref="Driver"/> seam) — and both hit the same server contracts (file import, publish-resource,
/// toggle-protocol, style save). The shape mirrors metadata-v2: one <see cref="Resource"/>, a list of
/// <see cref="Publications"/> (one per enabled protocol/service exposure), and a styled, gated go-live.
///
/// This is intentionally a mutable holder (the host component owns it for the lifetime of the flow) rather
/// than an immutable record graph, because the step components bind into it in place.
/// </summary>
public sealed class DataToPublishFlowState
{
    /// <summary>The chosen data source (step ①).</summary>
    public DataSourceSelection Source { get; set; } = new();

    /// <summary>The canonical resource produced (file/table/remote ingest) or selected (existing-resource path).</summary>
    public ResourceStaging Resource { get; set; } = new();

    /// <summary>The enabled protocols (step ③). Each maps to a metadata-v2 Publication via <see cref="PublishProtocolCatalog"/>.</summary>
    public List<string> EnabledProtocols { get; set; } = [.. PublishProtocolCatalog.DefaultEnabled];

    /// <summary>The auto-provisioned (editable) service slot the publications attach to.</summary>
    public string ServiceSlot { get; set; } = "service";

    /// <summary>The chosen style (step ④) — reuses the dual-mode StudioStyleEditor; null until a style is picked.</summary>
    public string? StyleId { get; set; }

    /// <summary>The style encoding the editor last authored in (MapLibre canonical or Esri drawingInfo).</summary>
    public StyleEncoding StyleEncoding { get; set; } = StyleEncoding.MapLibre;

    /// <summary>Go-live visibility (step ⑤).</summary>
    public PublishVisibility Visibility { get; set; } = PublishVisibility.Org;

    /// <summary>Which driver is filling the flow. AI leads; manual remains an always-available fallback.</summary>
    public FlowDriver Driver { get; set; } = FlowDriver.Ai;

    /// <summary>The current step in the rail.</summary>
    public FlowStep Step { get; set; } = FlowStep.AddData;

    /// <summary>The publications the enabled protocols imply for the current service slot (derived; never fabricated).</summary>
    public IReadOnlyList<PublishProtocolPlan> PlannedPublications =>
        PublishProtocolCatalog.PlanPublications(ServiceSlot, EnabledProtocols);

    /// <summary>
    /// True when the source is an already-existing resource, in which case the flow skips the ingest
    /// (Resource) step and lands directly on Publish (redesign §3 item 2 / §3.0).
    /// </summary>
    public bool SkipsIngest => Source.Mode == AddDataMode.ExistingResource;
}

/// <summary>The five data sources the unified flow accepts (redesign §3 item 2). Mirrors the intake sketch modes.</summary>
public enum AddDataMode
{
    File,
    Table,
    RemoteService,
    ExistingResource,
    Ai,
}

/// <summary>Which driver fills the flow object. AI is a structural seam for Phase 3 (the same flow object, an AI driver TODO).</summary>
public enum FlowDriver
{
    Manual,
    Ai,
}

/// <summary>The steps of the unified flow rail (redesign §3).</summary>
public enum FlowStep
{
    AddData,
    Resource,
    Publish,
    Style,
    GoLive,
    Done,
}

/// <summary>Style encoding the dual-mode editor authored in (redesign step ④; ADR-0002).</summary>
public enum StyleEncoding
{
    MapLibre,
    Esri,
}

/// <summary>Go-live visibility scope (redesign §5.6).</summary>
public enum PublishVisibility
{
    Private,
    Org,
    Public,
}

/// <summary>The chosen data source and its mode-specific reference (file name, connection id, URL, resource id, AI prompt).</summary>
public sealed class DataSourceSelection
{
    public AddDataMode Mode { get; set; } = AddDataMode.File;

    /// <summary>Mode-specific reference: file name (File), connection id (Table), URL (RemoteService), resource id (ExistingResource), prompt (Ai).</summary>
    public string? Reference { get; set; }

    /// <summary>For the Table mode: the selected schema-qualified table once picked.</summary>
    public string? TableKey { get; set; }
}

/// <summary>
/// The canonical resource the flow is publishing: an id (set after ingest or chosen for the existing-resource
/// path), a display name, and the inferred shape (geometry/SRS/feature count/fields) the Resource step shows.
/// </summary>
public sealed class ResourceStaging
{
    public string? ResourceId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? GeometryType { get; set; }

    public int? Srid { get; set; }

    public long FeatureCount { get; set; }

    public IReadOnlyList<string> Fields { get; set; } = [];

    /// <summary>Validation findings surfaced by ingest (e.g. unsupported format, missing geometry). Never fabricated.</summary>
    public IReadOnlyList<string> ValidationFindings { get; set; } = [];

    /// <summary>True once a resource exists (ingested or selected) so the flow can advance to Publish.</summary>
    public bool IsReady => !string.IsNullOrWhiteSpace(ResourceId);
}
