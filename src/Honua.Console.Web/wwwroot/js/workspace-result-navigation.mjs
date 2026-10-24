// Opening a verified result is an explicit human selection, not an agent command.
// The existing shared-draft reader remains responsible for rendering/editing it.
export async function openStudioExecutionResult(workspace, artifact, current, {
    navigate = path => window.Blazor?.navigateTo ? window.Blazor.navigateTo(path) : window.location.assign(path),
    present = task => window.dispatchEvent(new CustomEvent('honua-workspace-studio-task',{detail:task})),
    selectedResources = []
} = {}) {
    const requireCurrent = () => {
        if (!current()) throw new Error('The selected context changed. Open the result again.');
        const style=workspace.sharedStyleProvider?.();
        if (workspace.hasUnsavedWork() || style?.dirty || style?.pending)
            throw new Error('Save or discard your current edits before opening another result.');
    };
    requireCurrent();
    await workspace.authorityChangeGuard?.();
    requireCurrent();
    const authority=workspace.authorityKey(), workspaceId=workspace.workspaceId;
    const session={...workspace.session};
    const response=await workspace.request(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(artifact.draftId)}`,{
        method:'GET',credentials:'same-origin',headers:{
            'X-Honua-Environment':session.environmentId || '',
            'X-Honua-Session-Partition':session.principalId || ''
        }
    });
    requireCurrent();
    if (!response.ok) throw new Error('This result is unavailable with your current access. Check the original request.');
    const envelope=await response.json();
    const draft=envelope?.data;
    requireCurrent();
    if (draft?.draftId !== artifact.draftId || draft.itemId !== artifact.itemId
        || draft.envelope?.family !== artifact.family || !Number.isSafeInteger(draft.generation) || draft.generation < 0
        || draft.generation < artifact.generation
        || typeof draft.packageKey !== 'string' || !/^[A-Za-z0-9._-]{1,200}$/.test(draft.packageKey))
        throw new Error('The returned draft does not match the original result.');
    const task={kind:'studio-composition',id:draft.draftId,revision:draft.generation,packageKey:draft.packageKey};
    const selected=workspace.select(selectedResources,task);
    const generation=workspace.contextGeneration;
    await selected;
    if (workspace.workspaceId !== workspaceId || workspace.authorityKey() !== authority
        || workspace.contextGeneration !== generation || workspace.context?.task?.id !== task.id
        || workspace.hasUnsavedWork())
        throw new Error('The selected context changed. Open the result again when ready.');
    present(task);
    navigate('/studio');
}
