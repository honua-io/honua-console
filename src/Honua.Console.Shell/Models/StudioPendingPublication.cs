using Honua.Sdk.Abstractions.Operations;

namespace Honua.Console.Shell.Models;

public sealed record StudioPendingPublication(Guid ItemId, Guid VersionId, HonuaOperationHandle Operation, Guid? DraftId = null, long DraftGeneration = 0)
{
    public ConsoleProposalStatus? ProposalStatus { get; init; }

    public bool IsPending => ProposalStatus is not (ConsoleProposalStatus.Succeeded or ConsoleProposalStatus.Failed
        or ConsoleProposalStatus.Rejected or ConsoleProposalStatus.RolledBack);

    public string Message => IsPending
        ? $"Saved version {VersionId} awaiting approval. Proposal {Operation.ProposalId}. "
            + "A separate authorized reviewer must act in Approvals; submission did not change the published version."
        : $"Proposal {Operation.ProposalId}: {ProposalStatus}. Refresh reads the actual published version.";
}

public sealed record StudioFrozenDraft(Guid DraftId, Guid ItemId, Guid VersionId);
