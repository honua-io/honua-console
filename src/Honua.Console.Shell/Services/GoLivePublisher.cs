using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class GoLivePublisher(IServiceConfigurationOperation serviceConfiguration)
{
    // Matches honua-server's administrator role, so a private service stays readable by operators.
    private const string AdminRole = "admin";

    public async Task<GoLiveOutcome> ApplyAsync(
        DataToPublishFlowState flow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);

        var serviceName = flow.TargetService;
        var protocols = await EnableProtocolsAsync(serviceName, flow.PlannedPublications, cancellationToken)
            .ConfigureAwait(false);
        var visibility = await serviceConfiguration
            .UpdateAccessPolicyAsync(AccessPolicyFor(serviceName, flow.Visibility), cancellationToken)
            .ConfigureAwait(false);
        return new GoLiveOutcome(protocols, ToStepResult(visibility));
    }

    private async Task<IReadOnlyList<GoLiveProtocolResult>> EnableProtocolsAsync(
        string serviceName,
        IReadOnlyList<PublishProtocolPlan> plans,
        CancellationToken cancellationToken)
    {
        var updated = await serviceConfiguration.UpdateProtocolsAsync(
            new ServiceProtocolsCommand
            {
                ServiceName = serviceName,
                EnabledProtocols = plans.Select(plan => plan.ProtocolId).ToArray(),
            },
            cancellationToken).ConfigureAwait(false);
        return plans.Select(plan => new GoLiveProtocolResult(plan, ProtocolResult(plan, updated))).ToArray();
    }

    private static GoLiveStepResult ProtocolResult(PublishProtocolPlan plan, ServiceConfigurationResult updated)
    {
        if (!updated.Succeeded)
        {
            return ToStepResult(updated);
        }

        return updated.EnabledProtocols.Contains(plan.ProtocolId, StringComparer.Ordinal)
            ? new GoLiveStepResult(true, "Live", null)
            : new GoLiveStepResult(false, "Not enabled", "honua-server did not list this protocol as enabled after the update.");
    }

    private static ServiceAccessPolicyCommand AccessPolicyFor(string serviceName, PublishVisibility visibility) =>
        new()
        {
            ServiceName = serviceName,
            AllowAnonymous = visibility == PublishVisibility.Public,
            AllowedRoles = visibility == PublishVisibility.Private ? [AdminRole] : [],
        };

    private static GoLiveStepResult ToStepResult(ServiceConfigurationResult result) =>
        new(result.Succeeded, result.State, result.Detail);
}
