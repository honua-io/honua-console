// A follow operation may observe canonical state only while its original view and
// authority remain current. This guard does not authorize or execute a tool.
export function taskKey(task) {
    return task ? JSON.stringify([task.kind,task.id,task.revision,task.contentHash,task.impactHash,task.packageKey]) : '';
}
export function captureWorkspaceFollow(workspace, task, isCurrentView) {
    const generation=workspace?.contextGeneration;
    const workspaceId=workspace?.workspaceId;
    const authority=workspace?.session;
    const authorityKey=value=>JSON.stringify([value?.principalId,value?.environmentId,value?.releaseId,value?.sourcePathBase]);
    const originalAuthority=authorityKey(authority),originalTask=taskKey(task);
    return ()=>Boolean(workspace && isCurrentView() && workspace.workspaceId===workspaceId
        && workspace.contextGeneration===generation && authorityKey(workspace.session)===originalAuthority
        && taskKey(workspace.context?.task)===originalTask);
}

export function queueWorkspaceFollow(owner,workspace,task,isCurrentView,observe,onError) {
    const bindingKey=JSON.stringify([workspace?.workspaceId,workspace?.viewId,workspace?.contextGeneration,
        workspace?.session?.principalId,workspace?.session?.environmentId,workspace?.session?.releaseId,workspace?.session?.sourcePathBase,taskKey(task)]);
    if(owner.workspaceFollowPending && owner.workspaceFollowKey===bindingKey) return owner.workspaceFollowQueue;
    const current=captureWorkspaceFollow(workspace,task,isCurrentView);
    const epoch=owner.workspaceFollowEpoch=(owner.workspaceFollowEpoch || 0)+1;
    owner.workspaceFollowKey=bindingKey;owner.workspaceFollowPending=true;
    const stillCurrent=()=>owner.workspaceFollowEpoch===epoch && current();
    const operation=(owner.workspaceFollowQueue || Promise.resolve()).catch(()=>{}).then(async()=>{
        if(!stillCurrent()) return;
        try { await observe(stillCurrent); }
        catch(error) {if(stillCurrent()) onError(error);}
    });
    owner.workspaceFollowQueue=operation.finally(()=>{if(owner.workspaceFollowEpoch===epoch)owner.workspaceFollowPending=false;});
    return owner.workspaceFollowQueue;
}
