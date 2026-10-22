/** ProposalModels.RequestedByAgent is a nullable agent identifier, never a boolean. */
export function assertHumanProposer(proposal) {
    if (typeof proposal?.requestedBy !== 'string' || !proposal.requestedBy.trim()
        || (proposal.requestedByAgent !== undefined && proposal.requestedByAgent !== null))
        throw new Error('Canonical publication proposer must be a human principal without an agent identifier.');
}
