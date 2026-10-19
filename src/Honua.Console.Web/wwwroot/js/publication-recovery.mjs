const fields = new Set(['connectionId','schema','table','serviceName','layerName']);
const statuses = new Set(['Accepted','Completed','Queued','Running','RequiresApproval','DryRunRequired','Denied','Rejected','Cancelled','Failed','Indeterminate']);
const terminal = new Set(['Completed','DryRunRequired','Denied','Rejected','Cancelled','Failed']);
const identifier = value => typeof value === 'string' && value.length >= 1 && value.length <= 128 && !/[^A-Za-z0-9_-]/.test(value);
const candidates = value => Object.fromEntries(Object.entries(value || {}).filter(([key,item]) => fields.has(key) && typeof item === 'string'));
export class PublicationRecovery {
    constructor(workspace) { this.workspace = workspace; }
    key() { return `honua.workspace.pending-publication.${this.workspace.workspaceId}`; }
    read() {
        const raw = this.workspace.storage.getItem(this.key());
        if (!raw) return null;
        let record;
        try { if (raw.length > 65536) throw new Error(); record = JSON.parse(raw); } catch { throw new Error('The previous publication reference is unavailable. Review the original workspace before publishing.'); }
        if (record.schemaVersion !== 1 || !['workspaceId','targetId','draftId','commandId','viewId'].every(key=>identifier(record[key]))
            || record.workspaceId !== this.workspace.workspaceId || !Number.isSafeInteger(record.draftRevision) || record.draftRevision < 0
            || typeof record.authority !== 'string') throw new Error('The previous publication reference is invalid. Review the original workspace before publishing.');
        return record;
    }
    samePending(record) {
        const current = this.read();
        return Boolean(current && ['workspaceId','targetId','draftId','draftRevision','commandId','viewId','authority'].every(key => current[key] === record[key]));
    }
    bound(record) {
        const workspace = this.workspace;
        return record.workspaceId === workspace.workspaceId && record.targetId === workspace.context?.targetId
            && record.authority === workspace.authorityKey();
    }
    create(commandId, draft) {
        if (this.read()) throw new Error('Check the previous publication before submitting another command.');
        const workspace = this.workspace;
        const record = {schemaVersion:1,workspaceId:workspace.workspaceId,targetId:workspace.context?.targetId,
            draftId:draft.id,draftRevision:draft.revision,commandId,viewId:workspace.viewId,authority:workspace.authorityKey(),
            candidateFields:candidates(Object.fromEntries(workspace.dirty))};
        if (!identifier(record.targetId)) throw new Error('Connect the original workspace before publishing.');
        workspace.storage.setItem(this.key(), JSON.stringify(record));
        workspace.notify(workspace);
        return record;
    }
    persistCandidate() {
        const record = this.read(), workspace = this.workspace;
        if (!record || !this.bound(record) || workspace.draft?.id !== record.draftId) return;
        workspace.storage.setItem(this.key(), JSON.stringify({...record,candidateFields:candidates(Object.fromEntries(workspace.dirty))}));
    }
    draftReference() { const record = this.read(); return record && this.bound(record) ? record.draftId : undefined; }
    restoreCandidate(draft) {
        const record = this.read(), workspace = this.workspace;
        if (!record || !this.bound(record) || record.draftId !== draft.id || workspace.dirty.size) return;
        for (const [field,value] of Object.entries(candidates(record.candidateFields))) workspace.dirty.set(field,value);
    }
    observe(record, value) {
        const receipt = value?.data || value;
        if (receipt?.operationId !== 'service.publish' || !identifier(receipt.operationInstanceId) || !statuses.has(receipt.status)
            || record.operationInstanceId && record.operationInstanceId !== receipt.operationInstanceId)
            throw new Error('The previous publication receipt could not be verified. Its acceptance remains unknown.');
        const latest = this.read();
        if (!latest || !this.samePending(record) || !this.bound(latest)
            || latest.operationInstanceId && latest.operationInstanceId !== receipt.operationInstanceId) throw new Error('Restore the original publication connection before reviewing its receipt.');
        // A read in flight must not overwrite typing that arrived after that read began.
        this.workspace.storage.setItem(this.key(), JSON.stringify({...latest,operationInstanceId:receipt.operationInstanceId,lastStatus:receipt.status}));
        this.workspace.notify(this.workspace);
        return receipt;
    }
    async reconcile(isCurrent = () => true) {
        const record = this.read();
        if (!record) return null;
        const workspace = this.workspace, viewId = workspace.viewId;
        const current = () => isCurrent() && workspace.viewId === viewId && this.bound(record) && this.samePending(record);
        if (!current()) throw new Error('Restore the original publication connection before checking its command.');
        let value;
        try { value = await workspace.api(`${record.workspaceId}/commands/${record.commandId}?draftId=${encodeURIComponent(record.draftId)}&draftRevision=${record.draftRevision}`); }
        catch { throw new Error('Previous publication acceptance is unknown. Check its original command; do not submit another publication.'); }
        if (!current()) throw new Error('The publication connection changed during its read. The original command remains unresolved.');
        return this.observe(record,value);
    }
    async requireResolved(isCurrent = () => true) {
        if (!this.read()) return;
        const receipt = await this.reconcile(isCurrent);
        const error = new Error(terminal.has(receipt.status)
            ? `Previous publication ${receipt.status}. Review that result and your current draft before saving or publishing again.`
            : `Previous publication ${receipt.status}. Save and Publish are paused until its original command is resolved.`);
        error.receipt = receipt;
        throw error;
    }
    async acknowledge({readFields, isCurrentView = () => true}) {
        const workspace = this.workspace, record = this.read();
        if (!record) return;
        const draftId = workspace.draft?.id, revision = workspace.draft?.revision, viewId = workspace.viewId;
        const fieldSnapshot = JSON.stringify(candidates(readFields()));
        const dirtySnapshot = JSON.stringify(Object.fromEntries(workspace.dirty));
        const current = () => isCurrentView() && workspace.viewId === viewId && this.bound(record) && this.samePending(record)
            && workspace.draft?.id === draftId && workspace.draft?.revision === revision
            && JSON.stringify(candidates(readFields())) === fieldSnapshot && JSON.stringify(Object.fromEntries(workspace.dirty)) === dirtySnapshot;
        if (!current() || !draftId || draftId !== record.draftId) throw new Error('Review the current publish form before acknowledging the previous result.');
        const receipt = await this.reconcile(current);
        if (!terminal.has(receipt.status)) throw new Error('The original publication is not resolved. Review its operation or human approval.');
        const draft = await workspace.api(`${workspace.workspaceId}/drafts/${encodeURIComponent(draftId)}`);
        if (!current() || draft.id !== draftId || draft.revision !== revision || draft.kind !== 'publish-layer')
            throw new Error('The current draft changed during review. Your edits are retained; review again.');
        if (receipt.status !== 'Completed' && revision === record.draftRevision) {
            const advanced = await workspace.api(`${workspace.workspaceId}/drafts/${encodeURIComponent(draftId)}`, 'PATCH', {
                expectedRevision:revision,viewId,changes:{}
            });
            if (!current() || advanced.id !== draftId || advanced.kind !== 'publish-layer'
                || advanced.revision !== revision + 1)
                throw new Error('The reviewed draft revision could not be confirmed. Keep the original publication reference and review again.');
            const context = await workspace.api(`${workspace.workspaceId}?viewId=${encodeURIComponent(viewId)}`);
            if (!current() || context.targetId !== record.targetId || context.task?.id !== draftId
                || context.task?.revision !== advanced.revision || (context.task?.kind || 'publish-layer') !== 'publish-layer')
                throw new Error('The canonical publish context changed during review. Your edits and original publication reference are retained.');
            workspace.draft = advanced; workspace.context = context;
        }
        workspace.storage.setItem(this.key(), '');
        workspace.notify(workspace);
        return receipt;
    }
    commandKey(draft) {
        const workspace = this.workspace;
        return `honua.workspace.command.${workspace.workspaceId}.${draft.id}.${draft.revision}`;
    }
    isTerminal(record) { return Boolean(record && terminal.has(record.lastStatus)); }
}
