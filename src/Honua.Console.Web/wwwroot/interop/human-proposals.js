export async function decide({ action, profileId, authority, proposalId, decision }) {
    if (action !== 'review' && action !== 'status' && action !== 'approve') throw new Error('Unknown human decision.');
    const bootstrap = await fetch('/api/console/proposals/session', {
        credentials: 'same-origin', cache: 'no-store', redirect: 'error'
    });
    if (!bootstrap.ok) throw new Error('The interactive Console session is unavailable.');
    const session = await bootstrap.json();
    if (!session.csrfToken || session.profileId !== profileId) throw new Error('The proposal environment changed.');
    const response = await fetch(`/api/console/proposals/${encodeURIComponent(proposalId)}/${action}`, {
        method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': session.csrfToken },
        body: JSON.stringify({ profileId, authority, decision })
    });
    if (!response.ok) throw new Error('Reload the proposal to check its decision status.');
    return response.json();
}
