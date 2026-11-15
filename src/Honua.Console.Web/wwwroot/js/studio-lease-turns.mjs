// Let a human lifecycle action, or a source read Studio rejects once its lease
// rotates, finish on its captured lease before the host proactively rotates it.
// This never extends expiry or retries an action. A stalled read stops holding
// the lease after READ_HOLD_MS, so it fails alone instead of expiring the editor.
const turns = new WeakMap();
const reads = ['listRecipeSources', 'resolveRecipeSource'];
const methods = [...reads, 'createRecipeDraft', 'createWorkspaceDraft', 'attachWorkspaceDraft',
    'prepareWorkspaceTurn', 'saveWorkspaceVersion', 'reopenWorkspaceVersion', 'publishWorkspaceVersion',
    'createEditorDraft', 'attachEditorDraft', 'prepareEditorTurn', 'saveEditorVersion',
    'reopenEditorVersion', 'releaseEditorVersion'];
export const READ_HOLD_MS = 30_000;

export function trackStudioLeaseTurns(app) {
    if (turns.has(app)) return;
    const state = { pending: 0 };
    turns.set(app, state);
    for (const method of methods) {
        const original = app[method];
        if (typeof original !== 'function') continue;
        app[method] = function (...args) {
            state.pending++;
            let released = false, timer;
            const release = () => { if (!released) { released = true; state.pending--; clearTimeout(timer); } };
            let result;
            try { result = original.apply(this, args); }
            catch (error) { release(); throw error; }
            if (reads.includes(method)) timer = setTimeout(release, READ_HOLD_MS);
            // Observe completion while preserving the original promise and outcome.
            void Promise.resolve(result).then(release, release);
            return result;
        };
    }
}

export function hasPendingStudioLeaseTurn(app) {
    return Boolean(app && turns.get(app)?.pending);
}
