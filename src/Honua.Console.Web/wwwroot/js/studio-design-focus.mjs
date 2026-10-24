// Authoring focus is descriptive context, never a server resource or an authorization grant.
const identifier = value => typeof value === 'string' && value.length > 0 && value.length <= 512
    && !/[\u0000-\u001f\u007f]/.test(value);
const changed = () => { throw new Error('The selected Studio design changed. Review the draft and selection before sending again.'); };

export function captureStudioDesignFocus(app) {
    if (!app?.isConnected || !app.workspaceDraftIdentity) return undefined;
    const state = app.composition?.state;
    const selection = app.composition?.selection;
    if (!state || !Array.isArray(selection) || !selection.length) return undefined;
    if (selection.length > 32) changed();
    const draftId = app.workspaceDraftIdentity.draftId;
    const authority = JSON.stringify(app.workspaceCookieBinding);
    const targets = selection.map(target => {
        if (target.kind === 'feature') {
            if (!['string','number'].includes(typeof target.featureId)
                || typeof target.featureId === 'number' && !Number.isFinite(target.featureId)
                || !identifier(target.sourceId) || !identifier(String(target.featureId))) changed();
            return {kind:'feature', sourceId:target.sourceId, featureId:target.featureId};
        }
        const objects = {layer:state.layers, component:state.widgets, control:state.controls, region:state.annotations}[target.kind];
        const object = objects?.find(candidate => candidate.id === target.id);
        if (!object || !identifier(target.id)) changed();
        const sourceId = object.sourceId ?? object.config?.sourceId;
        if (sourceId !== undefined && !identifier(sourceId)) changed();
        return {kind:target.kind, id:target.id,
            ...(sourceId ? target.kind === 'layer' ? {sourceId} : {sourceReference:sourceId} : {})};
    });
    const selected = app.selectedDesignRelationship;
    let relationship;
    if (selected) {
        const entry = state.interactions.find(item => item.id === selected.interactionId);
        const owner = targets.some(target => target.kind !== 'feature'
            && `${target.kind === 'component' ? 'widget' : target.kind}:${target.id}` === selected.ownerRef);
        if (!entry || !owner || !identifier(selected.interactionId) || !identifier(selected.ownerRef)
            || (entry.on.ref !== selected.ownerRef && entry.do.ref !== selected.ownerRef)) changed();
        relationship = {interactionId:selected.interactionId, ownerRef:selected.ownerRef};
    }
    const label = targets.map(target => target.kind === 'feature'
        ? `feature ${target.sourceId}/${target.featureId}`
        : `${target.kind} ${target.id}${target.sourceId ? ` · source ${target.sourceId}` : ''}${target.sourceReference ? ` · source reference ${target.sourceReference}` : ''}`).join(', ')
        + (relationship ? ` · interaction ${relationship.interactionId}` : '');
    return {
        label,
        bind(task) {
            if (!app.isConnected || app.composition.state !== state
                || JSON.stringify(app.workspaceCookieBinding) !== authority
                || app.workspaceDraftIdentity?.draftId !== draftId
                || task?.kind !== 'studio-composition' || task.id !== draftId
                || task.revision !== app.workspaceDraftIdentity?.generation) changed();
            return {draftId, revision:task.revision, targets, ...(relationship ? {relationship} : {})};
        }
    };
}

export function studioDesignFocusLabel(provider) {
    try { return provider?.()?.label; }
    catch { return 'Selected design unavailable; review the inspector before sending.'; }
}

export function promptWithStudioDesignFocus(prompt, focus, task) {
    if (!focus) return prompt;
    const context = focus.bind(task);
    const result = `${prompt}\n\nSelected Studio authoring context (identifiers only; use the authorized draft):\n${JSON.stringify(context)}`;
    if (result.length > 16000) throw new Error('Shorten the message to leave room for the selected Studio design context.');
    return result;
}
