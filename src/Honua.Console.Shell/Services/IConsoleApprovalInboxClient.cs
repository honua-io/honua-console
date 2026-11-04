using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// The inbox is a projection ordered awaiting-approval first; approve and reject stay on the server gate, and an unbound environment is a missing-binding result rather than seeded rows.
public interface IConsoleApprovalInboxClient
{
    Task<OperateSectionResult<ApprovalInboxSnapshot>> GetInboxAsync(
        string? status = null,
        string? kind = null,
        CancellationToken cancellationToken = default);
}
