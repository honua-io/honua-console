// Human File objects stay in this document's memory. Only the verified metadata
// below is submitted to the canonical retained turn.
const clone = value => JSON.parse(JSON.stringify(value));
export const browserSelectionScope = workspace => workspace?.session && workspace?.context ? {
    environmentId:workspace.session.environmentId,principalId:workspace.session.principalId,
    releaseId:workspace.session.releaseId ?? null,sourcePathBase:workspace.session.sourcePathBase ?? null,
    workspaceId:workspace.workspaceId,targetId:workspace.context.targetId,viewId:workspace.viewId,
    contextRevision:workspace.context.revision
} : null;
const same = (a,b) => JSON.stringify(a) === JSON.stringify(b);
export class BrowserImportSelection {
    constructor({uuid = () => crypto.randomUUID()} = {}) { this.uuid = uuid; this.record = null; this.epoch = 0; }
    async prepare(file, workspace, {hashFile,signal,onProgress} = {}) {
        const scope = browserSelectionScope(workspace);
        if (!scope || !scope.workspaceId || !scope.targetId || !scope.environmentId || !scope.principalId)
            throw new Error('Connect the original workspace before choosing an import file.');
        if (!file || !Number.isSafeInteger(file.size) || file.size < 1 || typeof file.name !== 'string' ||
            file.name.length < 1 || file.name.length > 255 || file.name.trim() !== file.name || /[\u0000-\u001f\u007f-\u009f/\\]/.test(file.name))
            throw new Error('Choose a complete dataset with a bounded filename.');
        this.record = null; const epoch = ++this.epoch;
        const selectionId = this.uuid(), fileName = file.name, totalBytes = file.size;
        const expectedSha256 = await hashFile(file,{signal,onProgress});
        if (signal?.aborted || epoch !== this.epoch || !same(scope,browserSelectionScope(workspace)))
            throw new Error('The workspace changed while checking the file. Choose it again in the intended context.');
        if (typeof expectedSha256 !== 'string' || !/^[a-f0-9]{64}$/.test(expectedSha256) || expectedSha256.length !== 64)
            throw new Error('The complete-file digest could not be verified.');
        const metadata = Object.freeze({selectionId,fileName,totalBytes,expectedSha256});
        this.record = {scope:clone(scope),metadata,file,turnId:null}; return metadata;
    }
    forTurn(workspace) {
        if (!this.record || this.record.turnId) return null;
        if (!same(this.record.scope,browserSelectionScope(workspace)))
            throw new Error('The selected file belongs to an earlier context. Check the file again before sending.');
        return {scope:clone(this.record.scope),metadata:{...this.record.metadata}};
    }
    captured(workspace, turn, original) {
        if (!original || !this.record || !same(original.metadata,this.record.metadata)) return;
        if (turn.context?.workspaceId !== original.scope.workspaceId || turn.context.targetId !== original.scope.targetId ||
            turn.context.viewId !== original.scope.viewId || turn.context.revision !== original.scope.contextRevision ||
            !same(turn.browserFileSelection,original.metadata))
            throw new Error('The retained turn did not confirm the original selected file.');
        this.record.turnId = turn.turnId;
    }
    matchingFile(workspace, controller) {
        const record = this.record, scope = browserSelectionScope(workspace), task = controller.task;
        if (!record || !scope || !task || !record.turnId) return null;
        for (const key of ['environmentId','principalId','releaseId','sourcePathBase','workspaceId','targetId'])
            if (scope[key] !== record.scope[key]) return null;
        if (controller.record?.authority?.originatingTurnId !== record.turnId || task.source.kind !== 'browser-file' ||
            task.source.browserSelectionId !== record.metadata.selectionId || task.source.fileName !== record.metadata.fileName ||
            task.source.totalBytes !== record.metadata.totalBytes || task.source.expectedSha256 !== record.metadata.expectedSha256) return null;
        return record.file;
    }
    clear() { ++this.epoch; this.record = null; }
}
export const browserImportSelection = new BrowserImportSelection();
