using Honua.Sdk.Abstractions.Operations;

namespace Honua.Console.Shell.Models;

/// <summary>Console state binding a server approval operation to the saved version submitted by this editor.</summary>
public sealed record StudioPendingPublication(Guid ItemId, Guid VersionId, HonuaOperationHandle Operation)
{
    /// <summary>Truthful submission status; approval and publication remain separate server decisions.</summary>
    public string Message => $"Saved version awaiting approval. Proposal {Operation.ProposalId}. "
        + "A separate authorized reviewer must act in Approvals; the published version is unchanged.";
}
