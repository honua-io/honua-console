// References only: the server re-resolves these pins under the submitting principal.
export function readSharedStyleFocus(root) {
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
        task: valid ? Object.freeze({kind:'shared-style',id:value.styleTaskId,revision,
            contentHash:value.styleContentHash,impactHash:value.styleImpactHash}) : null,
        dirty:value.styleDirty === 'true', pending:value.stylePending === 'true'
    });
}
export function prepareSharedStyleFocus(focus) {
    if (!focus.task) throw new Error('Wait for the authorized shared-style review before submitting.');
    if (focus.pending) throw new Error('Wait for the shared-style read or save to finish before submitting.');
    if (focus.dirty) throw new Error('Save or discard your shared-style edits before submitting.');
    return focus.task;
}
