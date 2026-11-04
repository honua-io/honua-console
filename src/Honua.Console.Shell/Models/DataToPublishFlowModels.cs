namespace Honua.Console.Shell.Models;

// A mutable holder rather than an immutable record graph: the host component owns it for the lifetime of
// the flow and the step components bind into it in place.
public sealed class DataToPublishFlowState
{
    public DataSourceSelection Source { get; set; } = new();

    public ResourceStaging Resource { get; set; } = new();

    public List<string> EnabledProtocols { get; set; } = [.. PublishProtocolCatalog.DefaultEnabled];

    public string ServiceSlot { get; set; } = "service";

    public string? StyleId { get; set; }

    public PublishVisibility Visibility { get; set; } = PublishVisibility.Org;

    public FlowStep Step { get; set; } = FlowStep.AddData;

    public string TargetService => PublishProtocolCatalog.ServiceNameFor(ServiceSlot);

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

public enum FlowStep
{
    AddData,
    Resource,
    Publish,
    Style,
    GoLive,
}

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

    public bool LayerEnabled { get; set; } = true;
}
