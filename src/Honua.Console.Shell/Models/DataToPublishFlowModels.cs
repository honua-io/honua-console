namespace Honua.Console.Shell.Models;

// A mutable holder rather than an immutable record graph: the host component owns it for the lifetime of
// the flow and the step components bind into it in place.
public sealed class DataToPublishFlowState
{
    /// <summary>The chosen data source (step ①).</summary>
    public DataSourceSelection Source { get; set; } = new();

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

    /// <summary>The current step in the rail.</summary>
    public FlowStep Step { get; set; } = FlowStep.AddData;

    /// <summary>The publications the enabled protocols imply for the current service slot (derived; never fabricated).</summary>
    public IReadOnlyList<PublishProtocolPlan> PlannedPublications =>
        PublishProtocolCatalog.PlanPublications(ServiceSlot, EnabledProtocols);
}

public enum AddDataMode
{
    File,
    Table,
    RemoteService,
    ExistingResource,
}

/// <summary>The steps of the unified flow rail (redesign §3).</summary>
public enum FlowStep
{
    AddData,
    Resource,
    Publish,
    Style,
    GoLive,
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

public sealed class DataSourceSelection
{
    public AddDataMode Mode { get; set; } = AddDataMode.File;

    public string? Reference { get; set; }
}

public sealed class ResourceStaging
{
    public string? ResourceId { get; set; }

    public string Name { get; set; } = string.Empty;
}
