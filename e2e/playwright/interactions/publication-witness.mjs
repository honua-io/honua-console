/** ProposalModels.RequestedByAgent is a nullable agent identifier, never a boolean. */
export function assertHumanProposer(proposal) {
    if (typeof proposal?.requestedBy !== 'string' || !proposal.requestedBy.trim()
        || (proposal.requestedByAgent !== undefined && proposal.requestedByAgent !== null))
        throw new Error('Canonical publication proposer must be a human principal without an agent identifier.');
}

/** A pending publication can precede creation of its final request resource. */
export function publicationRequestId(operation, pendingId) {
    const valid = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
    if (pendingId !== undefined && !valid(pendingId)) throw new Error('Invalid pending publication request identifier.');
    const finalId = operation?.resourceIds?.requestId;
    if (!valid(finalId)) throw new Error('Final operation must identify its publication request.');
    if (pendingId !== undefined && pendingId !== finalId) throw new Error('Publication request changed after approval.');
    return finalId;
}
