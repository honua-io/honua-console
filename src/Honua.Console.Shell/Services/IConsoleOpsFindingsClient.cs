using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Interactive proposals use the operator bearer; an explicit sessionless headless profile does not.
public interface IConsoleOpsFindingsClient
{
    Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(
        CancellationToken cancellationToken = default);

    // A 404 means the finding's condition cleared or it has no recommended action, so the page refreshes the list instead of treating it as a hard failure.
    Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(
        string findingId,
        CancellationToken cancellationToken = default);
}
