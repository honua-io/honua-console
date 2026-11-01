// Let a human lifecycle action finish on its captured lease before the host
// proactively rotates it. This never extends expiry or retries an action.
const turns = new WeakMap();
const methods = ['createRecipeDraft', 'createWorkspaceDraft', 'attachWorkspaceDraft',
    'prepareWorkspaceTurn', 'saveWorkspaceVersion', 'reopenWorkspaceVersion', 'publishWorkspaceVersion'];

export function trackStudioLeaseTurns(app) {
    if (turns.has(app)) return;
    const state = { pending: 0 };
    turns.set(app, state);
    for (const method of methods) {
        const original = app[method];
        if (typeof original !== 'function') continue;
        app[method] = function (...args) {
            state.pending++;
            let result;
            try { result = original.apply(this, args); }
            catch (error) { state.pending--; throw error; }
            // Observe completion while preserving the original promise and outcome.
            void Promise.resolve(result).then(() => state.pending--, () => state.pending--);
            return result;
        };
    }
}

export function hasPendingStudioLeaseTurn(app) {
    return Boolean(app && turns.get(app)?.pending);
}
