using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// An unreachable source must not invent proposals, so the sources that still answer remain visible.
public interface IConsoleProposalSource
{
    ConsoleProposalSource Source { get; }

    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}
