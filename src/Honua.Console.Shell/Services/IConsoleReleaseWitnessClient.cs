namespace Honua.Console.Shell.Services;

/// <summary>
/// Read-only release-bound evidence projected into the authenticated Console UI.
/// This is deliberately narrower than an admin-form surface: it observes one
/// approved Studio publication and its audit/runtime joins.
/// </summary>
public interface IConsoleReleaseWitnessClient
{
    Task<OperateSectionResult<ConsoleReleaseWitnessEvidence>> ObserveAsync(
        ConsoleReleaseWitnessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ConsoleReleaseWitnessRequest(
    string Family,
    string ItemId,
    string VersionId,
    string ContentHash,
    string ProposalId);

public sealed record ConsoleReleaseWitnessEvidence(
    string ServerSourceRevision,
    string Family,
    string ItemId,
    string VersionId,
    string ContentHash,
    string ProposalId,
    string PublicationId,
    string PublicUrl,
    string AuditCorrelationId,
    string AuditExecutionOperationId,
    bool AuditVerified);
