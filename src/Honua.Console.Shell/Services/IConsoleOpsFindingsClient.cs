using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleOpsFindingsClient
{
    Task<OperateSectionResult<OpsFindingsListResponse>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<OperateSectionResult<OpsFindingProposeResponse>> ProposeAsync(
        string findingId,
        CancellationToken cancellationToken = default);
}
