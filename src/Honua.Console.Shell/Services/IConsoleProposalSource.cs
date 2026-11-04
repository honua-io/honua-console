using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IConsoleProposalSource
{
    ConsoleProposalSource Source { get; }

    Task<OperateSectionResult<IReadOnlyList<ConsoleProposalSummary>>> ListAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}
