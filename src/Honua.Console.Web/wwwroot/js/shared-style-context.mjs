const clientEdits = new WeakMap();
function clientState(editor) {
    const epoch = editor.dataset.styleObservationEpoch || '';
    let state = clientEdits.get(editor);
    if (!state || state.epoch !== epoch) {
        state = {epoch,dirty:false}; clientEdits.set(editor,state);
    }
    return state;
}
// An accepted canonical observation advances the epoch. Ordinary Razor rerenders
// cannot clear a browser edit before its input event reaches the server.
export function markSharedStyleClientDirty(editor) { clientState(editor).dirty = true; }
export function watchSharedStyleEdits(root, options = {}) {
    root.addEventListener('input', event => {
        const field = event.target?.closest?.('[data-style-content]');
        const editor = field?.closest?.('[data-shared-style-editor]');
        if (editor) markSharedStyleClientDirty(editor);
    }, {...options,capture:true});
}
// References only: the server re-resolves these pins under the submitting principal.
export function readSharedStyleFocus(root, expectedPartition) {
    const editor = root.querySelector('[data-shared-style-editor]');
    if (!editor) return undefined;
    const value = editor.dataset;
    const revision = Number(value.styleTaskRevision);
    const valid = typeof value.styleTaskId === 'string' && value.styleTaskId.length > 0
        && value.styleTaskId.length <= 256 && !/[\x00-\x1f\x7f]/.test(value.styleTaskId)
        && Number.isSafeInteger(revision) && revision > 0
        && /^[a-f0-9]{64}$/.test(value.styleContentHash || '')
        && /^[a-f0-9]{64}$/.test(value.styleImpactHash || '');
    return Object.freeze({
        task: valid && (!expectedPartition || value.styleAuthorityBinding === expectedPartition) ? Object.freeze({kind:'shared-style',id:value.styleTaskId,revision,
            contentHash:value.styleContentHash,impactHash:value.styleImpactHash}) : null,
        authorityBinding:value.styleAuthorityBinding || null,
        dirty:value.styleDirty === 'true' || clientState(editor).dirty, pending:value.stylePending === 'true'
    });
}
export function prepareSharedStyleFocus(focus, expectedPartition) {
    if (expectedPartition && focus.authorityBinding !== expectedPartition)
        throw new Error('The style editor belongs to another connection. Restore its original connection before submitting.');
    if (!focus.task) throw new Error('Wait for the authorized shared-style review before submitting.');
    if (focus.pending) throw new Error('Wait for the shared-style read or save to finish before submitting.');
    if (focus.dirty) throw new Error('Save or discard your shared-style edits before submitting.');
    return focus.task;
}
