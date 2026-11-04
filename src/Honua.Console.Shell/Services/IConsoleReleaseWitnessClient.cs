namespace Honua.Console.Shell.Services;

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
