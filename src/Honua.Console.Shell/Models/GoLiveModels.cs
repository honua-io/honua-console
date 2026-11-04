namespace Honua.Console.Shell.Models;

public sealed record GoLiveStepResult(bool Succeeded, string State, string? Detail);

public sealed record GoLiveProtocolResult(PublishProtocolPlan Plan, GoLiveStepResult Result);

public sealed record GoLiveOutcome(IReadOnlyList<GoLiveProtocolResult> Protocols, GoLiveStepResult Visibility)
{
    public bool AllLive => Visibility.Succeeded && Protocols.All(protocol => protocol.Result.Succeeded);
}
