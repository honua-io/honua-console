using Honua.Sdk.Abstractions.Operations;

namespace Honua.Console.Shell.Models;

/// <summary>Console state binding a server approval operation to the saved version submitted by this editor.</summary>
public sealed record StudioPendingPublication(Guid ItemId, Guid VersionId, HonuaOperationHandle Operation, Guid? DraftId = null, long DraftGeneration = 0)
{
    /// <summary>Latest proposal status read from the server, if refreshed.</summary>
    public ConsoleProposalStatus? ProposalStatus { get; init; }

    /// <summary>Whether this proposal is still awaiting a server terminal outcome.</summary>
    public bool IsPending => ProposalStatus is not (ConsoleProposalStatus.Succeeded or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected or ConsoleProposalStatus.RolledBack);

    /// <summary>Truthful submission status; approval and publication remain separate server decisions.</summary>
    public string Message => IsPending
        ? $"Saved version {VersionId} awaiting approval. Proposal {Operation.ProposalId}. "
            + "A separate authorized reviewer must act in Approvals; submission did not change the published version."
        : $"Proposal {Operation.ProposalId}: {ProposalStatus}. Refresh reads the actual published version.";
}
