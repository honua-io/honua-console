using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>Preserves distinct serving and manager approval sources for one bound environment.</summary>
public sealed class DualConsoleProposalsClient(
    IConsoleProposalsClient serving,
    IConsoleProposalsClient manager,
    IConsoleEnvironmentProfileStore profiles,
    IConsoleManagerEndpoint? endpoint) : IConsoleProposalsClient
{
    private const string ManagerPrefix = "manager:";

    private async Task<bool?> HasSeparateManager(CancellationToken cancellationToken)
    {
        if (endpoint is null) return false;
        var profile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null) return null;
        var target = endpoint.Resolve(profile.ServerBaseUri);
        if (target is null) return null;
        return new Uri(target.AbsoluteUri.TrimEnd('/') + "/") != new Uri(profile.ServerBaseUri.AbsoluteUri.TrimEnd('/') + "/");
    }

    /// <inheritdoc />
    public async Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null, string? kind = null, string? requestedBy = null, CancellationToken cancellationToken = default)
    {
        var separate = await HasSeparateManager(cancellationToken).ConfigureAwait(false);
        if (separate is null) return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(
            OperateSectionStatus.Unavailable, "The management endpoint is not bound to this environment.");
        if (separate == false) return await serving.ListAsync(status, kind, requestedBy, cancellationToken).ConfigureAwait(false);
        var servingTask = ListBounded(serving, status, kind, requestedBy, cancellationToken);
        var managerTask = ListBounded(manager, status, kind, requestedBy, cancellationToken);
        await Task.WhenAll(servingTask, managerTask).ConfigureAwait(false);
        var servingResult = await servingTask.ConfigureAwait(false);
        var managerResult = await managerTask.ConfigureAwait(false);
        if (!servingResult.IsAllowed && !managerResult.IsAllowed)
            return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(
                OperateSectionStatus.Unavailable, "Neither serving nor management proposal source is available.");
        var servingItems = servingResult.IsAllowed ? servingResult.Value ?? [] : [];
        if (servingItems.Any(item => item.ProposalId.StartsWith(ManagerPrefix, StringComparison.Ordinal)))
            return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(
                OperateSectionStatus.Unavailable, "A proposal identifier conflicts with the management namespace.");
        var items = servingItems.Concat((managerResult.IsAllowed ? managerResult.Value ?? [] : []).Select(item => item with
        {
            ProposalId = ManagerPrefix + item.ProposalId
        })).ToArray();
        var partial = !servingResult.IsAllowed || !managerResult.IsAllowed || servingResult.PartialResult || managerResult.PartialResult;
        return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(items, partial,
            partial ? "A proposal source is unavailable or returned partial results." : "");
    }

    private static async Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListBounded(
        IConsoleProposalsClient client, string? status, string? kind, string? requestedBy, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            return await client.ListAsync(status, kind, requestedBy, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Denied(
                OperateSectionStatus.Unavailable, "The proposal source did not respond in time.");
        }
    }

    /// <inheritdoc />
    public Task<OperateSectionResult<ConsoleProposalDetail>> GetAsync(string proposalId, CancellationToken cancellationToken = default) =>
        Route(proposalId, (client, id) => client.GetAsync(id, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperateSectionResult<ConsoleProposalDetail>> ApproveAsync(string proposalId, CancellationToken cancellationToken = default) =>
        Route(proposalId, (client, id) => client.ApproveAsync(id, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<OperateSectionResult<ConsoleProposalDetail>> RejectAsync(string proposalId, string reason, CancellationToken cancellationToken = default) =>
        Route(proposalId, (client, id) => client.RejectAsync(id, reason, cancellationToken), cancellationToken);

    private async Task<OperateSectionResult<ConsoleProposalDetail>> Route(
        string proposalId, Func<IConsoleProposalsClient, string, Task<OperateSectionResult<ConsoleProposalDetail>>> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        var separate = await HasSeparateManager(cancellationToken).ConfigureAwait(false);
        if (separate is null) return OperateSectionResult<ConsoleProposalDetail>.Denied(
            OperateSectionStatus.Unavailable, "The management endpoint is not bound to this environment.");
        var isManager = proposalId.StartsWith(ManagerPrefix, StringComparison.Ordinal);
        if (isManager && separate != true) return OperateSectionResult<ConsoleProposalDetail>.Denied(
            OperateSectionStatus.Unavailable, "The original management endpoint is unavailable.");
        var id = isManager ? proposalId[ManagerPrefix.Length..] : proposalId;
        if (string.IsNullOrWhiteSpace(id)) return OperateSectionResult<ConsoleProposalDetail>.Denied(
            OperateSectionStatus.Unavailable, "The proposal identifier is invalid.");
        var result = await action(isManager ? manager : serving, id).ConfigureAwait(false);
        return isManager && result.Value is { } detail
            ? result with { Value = detail with { ProposalId = ManagerPrefix + detail.ProposalId } }
            : result;
    }
}
