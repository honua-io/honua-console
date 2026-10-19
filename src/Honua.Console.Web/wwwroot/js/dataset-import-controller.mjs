const clone = value => JSON.parse(JSON.stringify(value));
const digest = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value) && value.length === 64;
export class DatasetImportPaused extends Error {
    constructor(message = 'Import is paused. Return to the original task and check its status.') { super(message); }
}
export function datasetImportBinding(workspace) {
    const task = workspace?.context?.task;
    const session = workspace?.session;
    if (task?.kind !== 'dataset-import' || !session?.environmentId || !session?.principalId || !workspace?.workspaceId ||
        !workspace.context?.targetId || typeof task.id !== 'string' || !task.id || !Number.isSafeInteger(task.revision) || task.revision < 1 || !digest(task.contentHash)) return null;
    return Object.freeze({environmentId:session.environmentId, principalId:session.principalId, releaseId:session.releaseId ?? null,
        sourcePathBase:session.sourcePathBase ?? null, workspaceId:workspace.workspaceId,
        targetId:workspace.context.targetId, task:Object.freeze({id:task.id, revision:task.revision, contentHash:task.contentHash})});
}
export const datasetImportKey = binding => JSON.stringify(['honua.dataset-import.v1', binding.environmentId, binding.principalId,
    binding.releaseId ?? null, binding.sourcePathBase ?? null, binding.targetId, binding.workspaceId, binding.task.id, binding.task.revision, binding.task.contentHash]);
const reference = binding => ({taskId:binding.task.id, revision:binding.task.revision, contentHash:binding.task.contentHash});
const taskSnapshot = task => ({taskId:task.taskId, revision:task.revision, contentHash:task.contentHash,
    ownerActorId:task.ownerActorId, tenantId:task.tenantId ?? null, targetId:task.targetId, workspaceId:task.workspaceId,
    destination:{schema:task.destination.schema, table:task.destination.table},
    source:{kind:task.source.kind, fileName:task.source.fileName, totalBytes:task.source.totalBytes, expectedSha256:task.source.expectedSha256 ?? null, browserSelectionId:task.source.browserSelectionId ?? null,
        artifact:task.source.artifact ? {projectId:task.source.artifact.projectId, snapshotId:task.source.artifact.snapshotId,
            snapshotDigest:task.source.artifact.snapshotDigest, runId:task.source.artifact.runId, path:task.source.artifact.path} : null},
    options:{loadMode:task.options.loadMode, targetSrid:task.options.targetSrid, sourceSrid:task.options.sourceSrid ?? null},
    uploadAdmissionDeadline:task.uploadAdmissionDeadline});
const authoritySnapshot = descriptor => ({executorActorId:descriptor.executorActorId, initiatingHumanActorId:descriptor.initiatingHumanActorId,
    originatingTurnId:descriptor.originatingTurnId, originalTurnHash:descriptor.originalTurnHash,
    authorityRevision:descriptor.authorityRevision, authorityFingerprint:descriptor.authorityFingerprint,
    policyContextFingerprint:descriptor.policyContextFingerprint, uploadAdmissionDeadline:descriptor.uploadAdmissionDeadline,
    preparedOperationId:descriptor.preparedOperationId ?? null});

// The only remote mutation implementation is the qualified shared SDK. This
// controller owns browser recovery metadata and presentation, never server authority.
export class DatasetImportController {
    constructor({binding, tasks, uploads, hashFile, store, isCurrent, notify = () => {}, uuid = () => crypto.randomUUID(), now = () => Date.now()}) {
        this.binding = clone(binding); this.key = datasetImportKey(binding); this.reference = Object.freeze(reference(binding));
        Object.assign(this, {tasks, uploads, hashFile, store, isCurrent, notify, uuid, now});
        this.abort = new AbortController(); this.stage = 'loading'; this.busy = false; this.record = null; this.file = null;
    }
    current() {
        if (this.abort.signal.aborted || !this.isCurrent()) throw new DatasetImportPaused();
    }
    emit(stage, message) { this.stage = stage; this.message = message; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    async initialize() {
        this.current(); this.task = null; this.capability = null; this.receiptAvailable = false;
        this.emit('loading', 'Checking the original import task…');
        const task = await this.tasks.readTask(this.reference, this.abort.signal); this.current();
        if (!task || task.workspaceId !== this.binding.workspaceId || task.targetId !== this.binding.targetId ||
            task.taskId !== this.reference.taskId || task.revision !== this.reference.revision || task.contentHash !== this.reference.contentHash)
            throw new DatasetImportPaused('This original import task is unavailable.');
        const descriptor = await this.tasks.readAuthority(this.reference, this.abort.signal); this.current();
        if (!descriptor || descriptor.executorActorId !== task.ownerActorId || !descriptor.initiatingHumanActorId ||
            descriptor.uploadAdmissionDeadline !== task.uploadAdmissionDeadline)
            throw new DatasetImportPaused('This import needs current executor authority. Ask the agent to check or renew the original task.');
        const capability = await this.uploads.capabilities(this.reference, this.abort.signal); this.current();
        if (!capability || typeof capability.importAcceptanceSupported !== 'boolean' || !Number.isSafeInteger(capability.maximumFileBytes) ||
            task.source.totalBytes > capability.maximumFileBytes)
            throw new DatasetImportPaused('This deployment cannot transfer this task’s file with its current limits.');
        if (!Number.isSafeInteger(task.source.totalBytes) || task.source.totalBytes < 1 || !Number.isFinite(Date.parse(task.uploadAdmissionDeadline)))
            throw new DatasetImportPaused('The original file intent is incomplete.');
        const snapshot = taskSnapshot(task), authority = authoritySnapshot(descriptor);
        let record = await this.store.load(this.key); this.current();
        if (record && JSON.stringify(record.task) !== JSON.stringify(snapshot))
            throw new DatasetImportPaused('The retained import intent changed. Check the original task before continuing.');
        if (record?.authority && (record.authority.executorActorId !== authority.executorActorId ||
            record.authority.initiatingHumanActorId !== authority.initiatingHumanActorId || record.authority.originatingTurnId !== authority.originatingTurnId ||
            record.authority.originalTurnHash !== authority.originalTurnHash || record.authority.policyContextFingerprint !== authority.policyContextFingerprint ||
            record.authority.uploadAdmissionDeadline !== authority.uploadAdmissionDeadline || record.authority.authorityRevision > authority.authorityRevision ||
            record.authority.authorityRevision === authority.authorityRevision && record.authority.authorityFingerprint !== authority.authorityFingerprint))
            throw new DatasetImportPaused('The original executor or policy binding changed.');
        if (!record) record = await this.store.save(this.key, 0, {task:snapshot, authority, receipt:null, pending:null});
        else if (JSON.stringify(record.authority) !== JSON.stringify(authority)) record = await this.store.save(this.key, record.revision, {...record, authority});
        this.current(); this.task = snapshot; this.capability = capability; this.record = record;
        await this.readOriginal(); this.current();
        return this;
    }
    checkReceipt(value) {
        if (value && (value.taskId !== this.task.taskId || value.taskRevision !== this.task.revision || value.taskContentHash !== this.task.contentHash ||
            value.totalBytes !== this.task.source.totalBytes || value.expiresAt !== this.task.uploadAdmissionDeadline))
            throw new DatasetImportPaused('The server receipt does not match the original file intent.');
    }
    async saveResponse(pendingId, value) {
        this.checkReceipt(value);
        const latest = await this.store.load(this.key);
        if (latest?.pending?.id !== pendingId) throw new DatasetImportPaused('Another view reconciled this request. Check the original receipt.');
        if (!this.resolved(latest.pending, value)) throw new DatasetImportPaused('The server has not confirmed the original requested transition.');
        this.record = await this.store.save(this.key, latest.revision, {...latest, receipt:value, pending:null});
        this.receiptAvailable = true;
    }
    resolved(pending, value) {
        if (!value) return false;
        if (value.state === 'cancelled' || pending.action === 'create') return true;
        if (pending.action === 'append') return value.version > pending.before.version && value.acceptedBytes >= pending.before.acceptedBytes + pending.bytes;
        if (pending.action === 'seal') return value.state === 'sealed';
        if (pending.action === 'source') return value.state === 'sealed' && Boolean(value.sourceRetention);
        return false;
    }
    async readOriginal() {
        this.current();
        const stored = await this.store.load(this.key); this.current();
        if (!stored) throw new DatasetImportPaused('The original browser recovery record is unavailable.');
        const value = stored.receipt
            ? await this.uploads.reconcile(this.reference, stored.receipt, this.abort.signal)
            : await this.uploads.read(this.reference, this.abort.signal);
        this.current(); this.checkReceipt(value);
        if (!value && stored.receipt) {
            this.receiptAvailable = false; this.record = stored;
            this.emit('paused', 'The original receipt is currently unavailable. Check its status; do not start another upload.'); return null;
        }
        if (stored.pending && !this.resolved(stored.pending, value)) {
            this.receiptAvailable = false; this.record = stored; this.emit('unknown', 'The last request is unresolved. Check the original status before retrying.'); return value;
        }
        const latest = await this.store.load(this.key); this.current();
        if (latest?.revision !== stored.revision || latest?.pending?.id !== stored.pending?.id)
            throw new DatasetImportPaused('Another view changed this import. Check its original status.');
        if (stored.pending) await this.saveResponse(stored.pending.id, value);
        else if (JSON.stringify(stored.receipt) !== JSON.stringify(value))
            this.record = await this.store.save(this.key, stored.revision, {...stored, receipt:value});
        else this.record = stored;
        this.current(); this.receiptAvailable = true; this.present(); return value;
    }
    present() {
        const receipt = this.record?.receipt;
        if (this.record?.pending) return this.emit('unknown', 'The last request is unresolved. Check the original status.');
        if (receipt?.state === 'cancelled') return this.emit('cancelled', 'This upload was cancelled. A new import needs a new reviewed task.');
        if (receipt?.sourceRetention?.expiresAt && Date.parse(receipt.sourceRetention.expiresAt) <= this.now())
            return this.emit('paused', 'The retained source receipt expired. Check the original task with the agent.');
        if (receipt?.sourceRetention) return this.emit('retained', 'Upload verified and a retained-source receipt recorded. Import acceptance has not been confirmed.');
        if (receipt?.state === 'sealed') return this.emit('sealed', 'File verified. Continue to retain it for import; no import job has been accepted here.');
        if (this.task?.source.kind !== 'browser-file') return this.emit('paused', 'This task uses a Processes artifact. Its direct streaming transfer is not available in this panel yet.');
        this.emit(this.file ? 'ready' : 'idle', this.file ? 'File checked. Ready to upload to the original destination.' : 'Choose the file for this import task.');
    }
    async verifyFile(file) {
        if (this.busy) throw new DatasetImportPaused('Wait for the current import action.');
        this.current(); this.file = null;
        if (this.task?.source.kind !== 'browser-file' || !digest(this.task.source.expectedSha256))
            throw new DatasetImportPaused('Ask the agent to prepare a verified browser-file source for this task.');
        if (file.size !== this.task.source.totalBytes) throw new Error('Choose the complete file named by this import task.');
        this.busy = true; this.progress = 0; this.emit('hashing', 'Checking the complete file…');
        try {
            const sha256 = await this.hashFile(file, {signal:this.abort.signal, onProgress:(bytes, total) => {
                this.current(); this.progress = total ? bytes / total : 1; this.emit('hashing', 'Checking the complete file…');
            }});
            this.current(); if (sha256 !== this.task.source.expectedSha256) throw new Error('This file differs from the original import source. Choose the original file.');
            this.file = file; this.present();
        } finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    mutable() {
        this.current();
        if (!this.receiptAvailable) throw new DatasetImportPaused('Check the original receipt before making another request.');
        if (this.record?.pending) throw new DatasetImportPaused('Reconcile the unresolved original request before continuing.');
        if (this.record?.authority?.preparedOperationId) throw new DatasetImportPaused('This task is awaiting import admission. Check its original receipt.');
        if (Date.parse(this.task.uploadAdmissionDeadline) <= this.now()) throw new DatasetImportPaused('The original upload window expired. Ask for a new reviewed task.');
    }
    async perform(action, operation, bytes = 0) {
        this.mutable();
        const pending = {id:this.uuid(), action, before:this.record.receipt ? clone(this.record.receipt) : null, bytes};
        this.record = await this.store.save(this.key, this.record.revision, {...this.record, pending});
        try { this.current(); }
        catch (error) {
            const latest = await this.store.load(this.key);
            if (latest?.pending?.id === pending.id) this.record = await this.store.save(this.key, latest.revision, {...latest, pending:null});
            throw error;
        }
        // Intent is durable before entering SDK hashing/authentication/network work.
        try {
            const value = await operation(this.abort.signal);
            await this.saveResponse(pending.id, value);
            this.current(); this.present(); return value;
        } catch (error) { this.emit('unknown', 'The request outcome is unresolved. Check the original status.'); throw error; }
    }
    async transfer() {
        if (this.busy) throw new DatasetImportPaused('Wait for the current import action.');
        this.busy = true;
        try {
            await this.initialize(); this.mutable();
            if ((!this.record.receipt || this.record.receipt.state === 'receiving') && !this.file)
                throw new Error('Check the original file again before resuming.');
            if (!this.record.receipt) await this.perform('create', signal => this.uploads.create(this.reference, signal));
            while (this.record.receipt?.state === 'receiving' && this.record.receipt.acceptedBytes < this.record.receipt.totalBytes) {
                this.mutable(); const original = clone(this.record.receipt);
                const end = Math.min(original.totalBytes, original.acceptedBytes + original.chunkBytes);
                const chunk = this.file.slice(original.acceptedBytes, end);
                this.emit('uploading', 'Uploading to the original destination…');
                await this.perform('append', signal => this.uploads.appendChunk(this.reference, original, chunk, signal), chunk.size);
            }
            if (this.record.receipt?.state === 'receiving') {
                const original = clone(this.record.receipt);
                await this.perform('seal', signal => this.uploads.seal(this.reference, original, signal));
            }
            if (this.record.receipt?.state === 'sealed' && !this.record.receipt.sourceRetention) {
                const original = clone(this.record.receipt); this.emit('retaining', 'Retaining the verified source for import…');
                await this.perform('source', signal => this.uploads.retainSource(this.reference, original, signal));
            }
            this.present();
        } finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    async cancel() {
        if (this.busy) throw new DatasetImportPaused('Pause the current action before cancelling.');
        this.busy = true;
        try {
            await this.initialize(); this.mutable();
            if (this.record.receipt?.state !== 'receiving') throw new DatasetImportPaused('Only an unsealed original upload can be cancelled.');
            const original = clone(this.record.receipt);
            await this.perform('cancel', signal => this.uploads.cancel(this.reference, original, signal));
        } finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    async recover() {
        if (this.busy) throw new DatasetImportPaused('Wait for the current import action.');
        this.busy = true;
        try { await this.initialize(); }
        finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    dirty() { return this.busy || Boolean(this.file && !this.record?.receipt?.sourceRetention) || Boolean(this.record?.pending); }
    dispose() { this.abort.abort(); this.file = null; }
}
