using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public sealed class ServerConsoleProposalSource : IConsoleProposalSource
{
    private readonly IConsoleProposalsClient _proposals;

    public ServerConsoleProposalSource(IConsoleProposalsClient proposals)
    {
        _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    }

    public ConsoleProposalSource Source => ConsoleProposalSource.Server;

    public async Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        var listed = await _proposals.ListAsync(status, kind, requestedBy: null, cancellationToken)
            .ConfigureAwait(false);

        if (!listed.IsAllowed)
        {
            return listed;
        }

        // Server summaries are ConsoleProposalSource.Server by construction (the record default);
        // re-tag defensively so the source contract holds regardless of how a summary was built.
        var tagged = (listed.Value ?? [])
            .Select(summary => summary with { Source = ConsoleProposalSource.Server })
            .ToArray();

        return OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>.Allowed(
            tagged,
            listed.PartialResult,
            listed.Message);
    }
}
