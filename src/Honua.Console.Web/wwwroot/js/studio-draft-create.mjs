import { capturedProcessFetch } from './cookie-workspace-transport.mjs';

import { studioAuthority, studioResourceId as validId } from './studio-workspace-authority.mjs';

const terminalStatuses = ['completed', 'rejected', 'failed', 'cancelled'];
const terminal = status => terminalStatuses.includes(status);
class UnreadableHandoffError extends Error {}
const validKey = value => typeof value === 'string' && /^[A-Za-z0-9._-]{1,200}$/.test(value);

export class StudioDraftCreateSession {
    constructor(app, { storage = app.workspace?.storage, uuid = () => crypto.randomUUID(), readOperation, isCurrent = () => true } = {}) {
        this.app = app; this.storage = storage; this.uuid = uuid; this.isCurrent = isCurrent;
        this.readOperation = readOperation ?? (async operationId => {
            const request = capturedProcessFetch(app.session, { current: () => app.session });
            const response = await request(`/api/processes/operations/handles/${encodeURIComponent(operationId)}`);
            if (!response.ok) throw new Error(`Operation status could not be read (${response.status}). The original action is retained.`);
            return response.json();
        });
    }
    dispose() { this.disposed = true; }
    context() {
        if (this.disposed || !this.isCurrent()) throw new Error('The workspace changed. The original New map action is retained.');
        if (!this.app.workspace?.workspaceId) throw new Error('Reconnect Studio before creating a map.');
        return JSON.stringify([studioAuthority(this.app), this.app.workspace.workspaceId]);
    }
    // Conversation IDs change on reload; retain the action under its durable owner and environment.
    storageKey() {
        this.context();
        return `honua.studio.create.${encodeURIComponent(studioAuthority(this.app))}`;
    }
    read() {
        const raw = this.storage?.getItem(this.storageKey());
        if (!raw) return null;
        let record;
        try { record = JSON.parse(raw); }
        catch { throw new UnreadableHandoffError('The original New map action is unreadable. Check Inbox before continuing.'); }
        if (!record || !validKey(record.packageKey) || !validId(record.commandId)
            || !['submitting', 'awaiting-approval', ...terminalStatuses].includes(record.status)
            || (record.status === 'awaiting-approval' && !record.receipt)
            || (record.status === 'completed' && !validId(record.draftId))
            || (record.receipt && (!validId(record.receipt.operationInstanceId) || !validId(record.receipt.proposalId))))
            throw new UnreadableHandoffError('The original New map action is unreadable. Check Inbox before continuing.');
        return record;
    }
    assertCurrent(context, draftId) {
        if (this.context() !== context || this.app.workspaceDraftIdentity?.draftId !== draftId)
            throw new Error('The workspace changed. The original New map action is retained.');
    }
    save(record, context) {
        if (this.context() !== context) throw new Error('The workspace changed.');
        if (!this.storage) throw new Error('Persistent workspace storage is required before creating a map.');
        this.storage.setItem(this.storageKey(), JSON.stringify(record));
    }
    submit(packageKey) {
        if (this.pending) return this.pending;
        const task = this.runSubmit(packageKey);
        this.pending = task;
        task.finally(() => { if (this.pending === task) this.pending = undefined; }).catch(() => {});
        return task;
    }
    async runSubmit(packageKey) {
        if (!validKey(packageKey)) throw new Error('Use 1–200 letters, numbers, dots, dashes or underscores for the package key.');
        const context = this.context(), previous = this.app.workspaceDraftIdentity?.draftId;
        let record = this.read();
        if (record && record.packageKey !== packageKey) {
            if (record.status !== 'completed') throw new Error('Resolve the original New map action before starting another.');
            record = null;
        }
        if (record?.status === 'completed') {
            await this.app.attachWorkspaceDraft(record.draftId, () => this.assertCurrent(context, previous));
            return record;
        }
        if (record && record.status !== 'submitting') return record;
        record ??= { packageKey, commandId: this.uuid(), status: 'submitting' };
        this.save(record, context);
        const result = await this.app.createWorkspaceDraft(record.packageKey, record.commandId);
        // Immediate creation attaches inside Studio; only authority must stay stable in that case.
        if (this.context() !== context) throw new Error('The workspace changed.');
        if (result?.status === 'awaiting-approval' && validId(result.operationInstanceId) && validId(result.proposalId)) {
            record = { ...record, status: 'awaiting-approval', receipt: result };
        } else if (validId(result?.draftId)) record = { ...record, status: 'completed', draftId: result.draftId };
        else throw new Error('The create action returned no durable receipt. Retry the original action.');
        this.save(record, context);
        return record;
    }
    acknowledgeUnreadable() {
        this.context();
        try { this.read(); }
        catch (error) {
            if (!(error instanceof UnreadableHandoffError)) throw error;
            this.storage.removeItem(this.storageKey()); return;
        }
        throw new Error('The original action is readable; resolve its outcome first.');
    }
    acknowledge() {
        const record = this.read();
        if (!record || !terminal(record.status)) throw new Error('The original New map action has no confirmed outcome.');
        this.storage.removeItem(this.storageKey());
    }
    async refresh() {
        const context = this.context(), previous = this.app.workspaceDraftIdentity?.draftId;
        const record = this.read();
        if (!record?.receipt || record.status !== 'awaiting-approval') return record;
        const response = await this.readOperation(record.receipt.operationInstanceId);
        this.assertCurrent(context, previous);
        const operation = response?.data ?? response;
        if (operation?.operationInstanceId !== record.receipt.operationInstanceId) throw new Error('The operation receipt does not match the original action.');
        if ([1, 'Completed', 'completed'].includes(operation.status)) {
            const draftId = operation.resourceIds?.draftId;
            if (!validId(draftId)) throw new Error('The completed operation has no canonical draft ID. The original action is retained.');
            await this.app.attachWorkspaceDraft(draftId, () => this.assertCurrent(context, previous));
            if (this.context() !== context) throw new Error('The workspace changed.');
            const completed = { ...record, status: 'completed', draftId };
            this.save(completed, context);
            return completed;
        }
        const terminalStatusByOperationStatus = new Map([[6, 'rejected'], [7, 'rejected'], [8, 'cancelled'], [9, 'failed'],
            ['Denied', 'rejected'], ['Rejected', 'rejected'], ['Cancelled', 'cancelled'], ['Failed', 'failed']]);
        if (terminalStatusByOperationStatus.has(operation.status)) {
            const finished = { ...record, status: terminalStatusByOperationStatus.get(operation.status) };
            this.save(finished, context);
            return finished;
        }
        return record;
    }
}

export function mountStudioDraftCreate({ app, host }) {
    const session = new StudioDraftCreateSession(app, { isCurrent: () => host.isConnected && app.isConnected });
    const form = document.createElement('form');
    const key = document.createElement('input');
    key.required = true; key.maxLength = 200; key.pattern = '(?:[A-Za-z0-9._]|-){1,200}';
    key.placeholder = 'Map package key'; key.setAttribute('aria-label', 'New map package key');
    const create = document.createElement('button'); create.type = 'submit'; create.textContent = 'New map';
    const notice = document.createElement('p'); notice.setAttribute('role', 'status');
    const inbox = document.createElement('a'); inbox.textContent = 'Review approval in Inbox';
    const refresh = document.createElement('button'); refresh.type = 'button'; refresh.textContent = 'Check approval and open map';
    const acknowledge = document.createElement('button'); acknowledge.type = 'button'; acknowledge.textContent = 'Acknowledge outcome and start another map';
    const reset = document.createElement('button'); reset.type = 'button'; reset.textContent = 'Forget unreadable local handoff';
    form.append(key, create, notice, inbox, refresh, acknowledge, reset);
    host.querySelector('[data-studio-mount]').before(form);
    let disposed = false, busy = false;
    const render = () => {
        if (disposed) return;
        inbox.hidden = refresh.hidden = acknowledge.hidden = reset.hidden = true;
        create.disabled = key.disabled = busy;
        try {
            const record = session.read();
            if (!record) { notice.textContent = ''; create.textContent = 'New map'; return; }
            if (record.status !== 'completed' || !key.value) key.value = record.packageKey;
            key.disabled = busy || record.status !== 'completed';
            create.disabled = busy || record.status !== 'submitting' && record.status !== 'completed';
            create.textContent = record.status === 'submitting' ? 'Retry original New map' : record.status === 'completed' && key.value.trim() === record.packageKey ? 'Open map' : 'New map';
            acknowledge.hidden = !terminal(record.status); acknowledge.disabled = busy;
            notice.textContent = record.status === 'awaiting-approval'
                ? `Waiting for separate human approval. Proposal ${record.receipt.proposalId}; operation ${record.receipt.operationInstanceId}.`
                : record.status === 'completed' ? 'Shared server draft opened.'
                    : record.status === 'submitting' ? 'The original New map action is retained. Retry to recover its result.'
                        : `New map ${record.status}. The original action is retained.`;
            if (record.receipt) {
                inbox.hidden = false;
                inbox.href = `/inbox?proposalId=${encodeURIComponent(record.receipt.proposalId)}`;
                refresh.hidden = record.status !== 'awaiting-approval'; refresh.disabled = busy;
            }
        } catch (error) { notice.textContent = error.message; create.disabled = true; reset.hidden = !(error instanceof UnreadableHandoffError); reset.disabled = busy; }
    };
    const run = async action => {
        if (busy || disposed || !host.isConnected || !app.isConnected) return;
        busy = true; render();
        let failure;
        try { await action(); }
        catch (error) { failure = error; }
        finally { busy = false; if (!disposed) { render(); if (failure) notice.textContent = failure.message; } }
    };
    key.addEventListener('input', render);
    form.addEventListener('submit', event => {
        event.preventDefault();
        const packageKey = key.value.trim();
        if (form.reportValidity()) void run(() => session.submit(packageKey));
    });
    refresh.addEventListener('click', () => void run(() => session.refresh()));
    acknowledge.addEventListener('click', () => void run(() => { session.acknowledge(); key.value = ''; }));
    reset.addEventListener('click', () => {
        if (confirm('The server action may still complete. Check Inbox before starting another map. Forget this unreadable local handoff?'))
            void run(() => { session.acknowledgeUnreadable(); key.value = ''; });
    });
    render();
    return { dispose() { disposed = true; session.dispose(); form.remove(); } };
}
