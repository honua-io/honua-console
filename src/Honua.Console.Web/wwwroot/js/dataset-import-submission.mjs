import {datasetImportKey, DatasetImportPaused} from './dataset-import-controller.mjs';
const copy = value => structuredClone(value);
const sameReviewedReceipt = (reviewed, receipt) => receipt && receipt.state !== 'not-prepared' &&
    receipt.authorityRevision === reviewed.authorityRevision && receipt.authorityFingerprint === reviewed.authorityFingerprint &&
    receipt.policyContextFingerprint === reviewed.policyContextFingerprint &&
    Date.parse(receipt.acceptBefore) === Date.parse(reviewed.uploadAdmissionDeadline) &&
    (!reviewed.preparedOperationId || receipt.operationId === reviewed.preparedOperationId);

// Owns browser recovery metadata only. Canonical SDK owns network and receipt validation;
// server owns task/executor/initiator authority, preparation and durable acceptance.
export class DatasetImportSubmissionController {
    constructor({binding, submissions, store, isCurrent, notify = () => {}, uuid = () => crypto.randomUUID()}) {
        this.binding = binding;
        this.reference = Object.freeze({taskId:binding.task.id, revision:binding.task.revision, contentHash:binding.task.contentHash});
        this.key = `${datasetImportKey(binding)}:submission`;
        Object.assign(this, {submissions, store, isCurrent, notify, uuid});
        this.abort = new AbortController(); this.busy = false; this.record = null;
    }
    current() {
        if (this.abort.signal.aborted || !this.isCurrent()) throw new DatasetImportPaused('Return to the original import task to check its status.');
    }
    async read() {
        this.current();
        let original = await this.store.load(this.key); this.current();
        if (!original) original = await this.store.save(this.key, 0, {reference:this.reference, receipt:null, pending:null});
        this.current();
        if (JSON.stringify(original.reference) !== JSON.stringify(this.reference)) throw new DatasetImportPaused('The original submission binding changed.');
        const receipt = original.receipt
            ? await this.submissions.reconcile(this.reference, original.receipt, this.abort.signal)
            : await this.submissions.read(this.reference, this.abort.signal);
        this.current();
        const latest = await this.store.load(this.key); this.current();
        if (!latest || latest.revision !== original.revision || latest.pending?.id !== original.pending?.id)
            throw new DatasetImportPaused('Another view changed this submission. Check the original status again.');
        if (!receipt && (original.receipt || original.pending)) {
            this.record = latest; this.available = false; this.notify(this); return null;
        }
        const resolved = !original.pending || sameReviewedReceipt(original.pending.reviewed, receipt);
        this.record = await this.store.save(this.key, latest.revision, {...latest, receipt:receipt ?? latest.receipt,
            pending:resolved ? null : latest.pending});
        this.current(); this.available = true; this.notify(this); return receipt;
    }
    async prepare(reviewed) {
        if (this.busy) throw new DatasetImportPaused('Wait for the current submission action.');
        this.current();
        // Pin the displayed review before the first read/authentication await.
        const review = copy(reviewed);
        if (review.taskId !== this.reference.taskId || review.taskRevision !== this.reference.revision ||
            review.taskContentHash !== this.reference.contentHash) throw new DatasetImportPaused('Review the original import task first.');
        this.busy = true; this.notify(this);
        try {
            await this.read(); this.current();
            if (!this.available || this.record.pending) throw new DatasetImportPaused('The original submission is unresolved. Check its status; do not submit again.');
            if (this.record.receipt && this.record.receipt.state !== 'not-prepared') return this.record.receipt;
            const pending = {id:this.uuid(), reviewed:review};
            this.record = await this.store.save(this.key, this.record.revision, {...this.record, pending});
            try { this.current(); }
            catch (error) {
                const latest = await this.store.load(this.key);
                if (latest?.pending?.id === pending.id)
                    this.record = await this.store.save(this.key, latest.revision, {...latest, pending:null});
                throw error;
            }
            // No retry here. Any unknown result retains this original reviewed intent.
            const receipt = await this.submissions.submit(this.reference, review, this.abort.signal);
            this.current();
            if (!sameReviewedReceipt(review, receipt)) throw new DatasetImportPaused('The server has not confirmed the original reviewed preparation.');
            const latest = await this.store.load(this.key); this.current();
            if (latest?.pending?.id !== pending.id) throw new DatasetImportPaused('Another view reconciled this submission. Check its status.');
            this.record = await this.store.save(this.key, latest.revision, {...latest, receipt, pending:null});
            this.current(); this.available = true; return receipt;
        } finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    async recover() {
        if (this.busy) throw new DatasetImportPaused('Wait for the current submission action.');
        this.busy = true; this.notify(this);
        try { return await this.read(); }
        finally { this.busy = false; if (!this.abort.signal.aborted && this.isCurrent()) this.notify(this); }
    }
    message() {
        const receipt = this.record?.receipt;
        if (this.busy) return 'Checking the original import submission…';
        if (this.record?.pending || this.available === false) return 'The submission outcome is unresolved. Check this original task’s status; do not submit again.';
        if (receipt?.state === 'prepared') return 'Import prepared. Awaiting the actual agent to submit this original task.';
        if (receipt?.state !== 'accepted') return 'No accepted import job has been confirmed.';
        const job = receipt.job;
        if (job.status === 'succeeded' && job.output) return `Imported ${job.output.featureCount.toLocaleString()} features into ${job.output.schema}.${job.output.physicalTableName}.`;
        if (job.status === 'failed') return 'The accepted import failed. Its original job receipt is retained.';
        if (job.status === 'cancelled') return 'The accepted import was cancelled. Its original job receipt is retained.';
        return `Import accepted · ${job.status}. Check this task for updated status.`;
    }
    dirty() { return this.busy || Boolean(this.record?.pending); }
    dispose() { this.abort.abort(); }
}
