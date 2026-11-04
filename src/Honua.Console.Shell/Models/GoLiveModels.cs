namespace Honua.Console.Shell.Models;

public sealed record GoLiveStepResult(bool Succeeded, string State, string? Detail);

public sealed record GoLiveProtocolResult(PublishProtocolPlan Plan, GoLiveStepResult Result);

public sealed record GoLiveOutcome(
    IReadOnlyList<GoLiveProtocolResult> Protocols,
    GoLiveStepResult Visibility,
    GoLiveStepResult Layer)
{
    public bool AllLive =>
        Layer.Succeeded && Visibility.Succeeded && Protocols.All(protocol => protocol.Result.Succeeded);
}

// What Apply & publish was asked to do, captured when the operator clicks it so later edits to the flow
// cannot change a request already in flight.
public sealed record GoLiveIntent(
    string ServiceName,
    IReadOnlyList<PublishProtocolPlan> Plans,
    PublishVisibility Visibility,
    bool LayerEnabled)
{
    public static GoLiveIntent From(DataToPublishFlowState flow) =>
        new(flow.TargetService, flow.PlannedPublications.ToArray(), flow.Visibility, flow.Resource.LayerEnabled);
}
