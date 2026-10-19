const id = value => typeof value === 'string' && value.length > 0 && value.length <= 128 && !/[^A-Za-z0-9_-]/.test(value);
const audiences = new Set(['personal', 'team', 'organization', 'public']);
const terminal = receipt => receipt?.status === 'accepted' || receipt?.status === 'rejected';

/** Retains submission identity before dispatch, including across a document reload. No credentials are stored. */
export class StudioPublicationSession {
    constructor(app, { storage = app.workspace?.storage, uuid = () => crypto.randomUUID() } = {}) {
        this.app = app; this.storage = storage; this.uuid = uuid;
    }
    authority() {
        const binding = this.app.workspaceCookieBinding;
        if (!binding?.principalId || !binding.environmentId) throw new Error('Reconnect Studio before publishing.');
        return JSON.stringify([binding.principalId, binding.environmentId, binding.sourcePathBase || '']);
    }
    key() {
        const workspaceId = this.app.workspace?.workspaceId;
        if (!id(workspaceId) || !this.storage) throw new Error('Connect a persistent workspace before publishing.');
        return `honua.workspace.studio-publication.${workspaceId}`;
    }
    context() { return JSON.stringify([this.key(), this.authority(), this.app.workspaceDraftIdentity?.draftId]); }
    read() {
        const key = this.key();
        const raw = this.storage.getItem(key);
        if (!raw) return null;
        let record;
        try { if (raw.length > 16384) throw new Error(); record = JSON.parse(raw); } catch { throw new Error('The original publication reference is unreadable. Restore it before submitting another publication.'); }
        if (record.schemaVersion !== 1 || !['draftId', 'itemId', 'versionId', 'commandId'].every(key => id(record[key]))
            || typeof record.context !== 'string' || typeof record.contentHash !== 'string' || !record.contentHash
            || !this.validInput(record.input)) throw new Error('The original publication reference is invalid. Review it before publishing again.');
        return record;
    }
    validInput(input) {
        return input && typeof input.intent?.route === 'string' && input.intent.route.startsWith('/')
            && !input.intent.route.startsWith('//') && input.intent.route.length <= 1024
            && !/[\s\\]/.test(input.intent.route) && audiences.has(input.intent.visibility)
            && (input.warningAcknowledgement === undefined || typeof input.warningAcknowledgement === 'string' && input.warningAcknowledgement.length <= 2000);
    }
    bound(record) { return record.context === this.context(); }
    async review(versionId) {
        if (this.read()) throw new Error('Resolve the original publication before starting another.');
        const context = this.context();
        const version = await this.app.reviewWorkspacePublication(versionId);
        if (context !== this.context()) throw new Error('The selected map or session changed during review.');
        this.reviewed = { version, context };
        return version;
    }
    submit(input) {
        if (this.pending) return this.pending;
        return this.run(async () => {
            if (this.read()) throw new Error('Use the original submission to recover its outcome.');
            const reviewed = this.reviewed;
            if (!reviewed || reviewed.context !== this.context()) throw new Error('Review this saved version before publishing.');
            const canonical = { intent: { route: input.intent?.route, visibility: input.intent?.visibility },
                ...(input.warningAcknowledgement ? { warningAcknowledgement: input.warningAcknowledgement } : {}) };
            if (!this.validInput(canonical)) throw new Error('Choose an audience and a local publication route beginning with /.');
            const version = reviewed.version;
            const record = { schemaVersion: 1, context: reviewed.context, draftId: this.app.workspaceDraftIdentity.draftId,
                itemId: version.itemId, versionId: version.versionId, contentHash: version.contentHash,
                commandId: this.uuid(), input: canonical };
            if (!id(record.commandId)) throw new Error('The publication command identity is invalid.');
            this.storage.setItem(this.key(), JSON.stringify(record)); // Must succeed BEFORE the mutation.
            return this.dispatch(record, version);
        });
    }
    retry() {
        if (this.pending) return this.pending;
        return this.run(async () => {
            const record = this.read();
            if (!record || !this.bound(record)) throw new Error('Restore the original map, account and environment before retrying.');
            const version = this.reviewed?.context === record.context && this.reviewed.version.versionId === record.versionId
                ? this.reviewed.version : await this.app.reviewWorkspacePublication(record.versionId);
            if (!this.bound(record) || version.itemId !== record.itemId || version.contentHash !== record.contentHash || version.versionId !== record.versionId)
                throw new Error('The original saved version could not be verified. No new publication was submitted.');
            return this.dispatch(record, version);
        });
    }
    async dispatch(record, version) {
        const key = this.key();
        const receipt = await this.app.publishWorkspaceVersion(version, record.input, record.commandId);
        if (!this.bound(record) || this.read()?.commandId !== record.commandId)
            throw new Error('The workspace changed. The original submission reference is retained for recovery.');
        const pending = receipt?.status === 'awaiting-approval' && id(receipt.operationInstanceId) && id(receipt.proposalId);
        const completed = terminal(receipt) && receipt.itemId === record.itemId && receipt.versionId === record.versionId && id(receipt.requestId);
        if (!pending && !completed) throw new Error('The publication outcome is unknown. Retain and retry the original submission.');
        const retained = { status: receipt.status, ...(pending
            ? { operationInstanceId: receipt.operationInstanceId, proposalId: receipt.proposalId }
            : { requestId: receipt.requestId }) };
        this.storage.setItem(key, JSON.stringify({ ...record, receipt: retained }));
        return receipt;
    }
    async run(action) {
        const pending = Promise.resolve().then(action);
        this.pending = pending;
        try { return await pending; } finally { this.pending = undefined; }
    }
    acknowledge() {
        const record = this.read();
        if (!record || !this.bound(record) || !terminal(record.receipt)) throw new Error('The original publication has not reached a confirmed outcome.');
        this.storage.removeItem(this.key());
        this.reviewed = undefined;
    }
}

export function mountStudioPublicationControls({ app, host, status }) {
    const session = new StudioPublicationSession(app);
    const section = document.createElement('section');
    section.setAttribute('aria-label', 'Map publication');
    section.dataset.studioPublication = '';
    const notice = document.createElement('p');
    const form = document.createElement('form');
    const summary = document.createElement('p');
    const route = document.createElement('input');
    route.required = true; route.maxLength = 1024; route.setAttribute('aria-label', 'Publication route');
    const routeField = document.createElement('label'); routeField.append('Publication route ', route);
    const audience = document.createElement('select');
    audience.setAttribute('aria-label', 'Publication audience');
    const audienceField = document.createElement('label'); audienceField.append('Who can see this map? ', audience);
    for (const [value, label] of [['personal', 'Only me'], ['team', 'My team'], ['organization', 'My organization'], ['public', 'Public']]) {
        const option = document.createElement('option'); option.value = value; option.textContent = label; audience.append(option);
    }
    const warning = document.createElement('textarea');
    warning.maxLength = 2000; warning.setAttribute('aria-label', 'Validation warning acknowledgement');
    const warningField = document.createElement('label'); warningField.append('Acknowledge the validation warnings ', warning);
    const diagnostics = document.createElement('ul'); diagnostics.setAttribute('aria-label', 'Publication validation warnings');
    const publish = document.createElement('button'); publish.type = 'submit'; publish.textContent = 'Publish reviewed version';
    const retry = document.createElement('button'); retry.type = 'button'; retry.textContent = 'Retry original submission';
    const acknowledge = document.createElement('button'); acknowledge.type = 'button'; acknowledge.textContent = 'Acknowledge publication outcome';
    const inbox = document.createElement('a'); inbox.href = '/inbox'; inbox.textContent = 'Review approvals';
    const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel publication review';
    for (const field of [route, audience, warning]) field.className = 'console-input';
    for (const label of [routeField, audienceField, warningField]) label.className = 'publish-field';
    for (const button of [publish, retry, acknowledge, cancel]) button.className = 'console-button console-button-secondary';
    publish.className = 'console-button';
    const fields = document.createElement('div'); fields.className = 'publish-field-grid'; fields.append(routeField, audienceField);
    const actions = document.createElement('div'); actions.className = 'console-action-row'; actions.append(publish, cancel);
    form.append(summary, fields, diagnostics, warningField, actions);
    section.append(notice, form, retry, acknowledge, inbox);
    const canvas = host.querySelector('[data-studio-mount]');
    if (canvas) canvas.before(section); else host.append(section);
    let busy = false, disposed = false, selectedContext, hasReview = false;
    const active = captured => {
        try { return !disposed && host.isConnected && app.isConnected && session.context() === captured; }
        catch { return false; }
    };
    function update() {
        if (disposed) return;
        form.hidden = retry.hidden = acknowledge.hidden = inbox.hidden = true;
        try {
            const context = session.context(), record = session.read();
            if (selectedContext !== context) { hasReview = false; selectedContext = context; }
            section.hidden = !record && !hasReview;
            notice.textContent = '';
            if (record) {
                if (!session.bound(record)) notice.textContent = 'Restore the original map, account and environment to recover its publication.';
                else {
                    notice.textContent = record.receipt?.status === 'awaiting-approval'
                        ? `Awaiting separate human approval. Proposal ${record.receipt.proposalId}.`
                        : record.receipt?.status === 'accepted' ? 'The reviewed version was published.'
                        : record.receipt?.status === 'rejected' ? 'The publication was rejected.'
                        : 'The submission outcome is unknown. Its original command and settings are retained.';
                    retry.hidden = terminal(record.receipt);
                    acknowledge.hidden = !terminal(record.receipt);
                    inbox.hidden = record.receipt?.status !== 'awaiting-approval';
                }
            } else form.hidden = !hasReview;
        } catch (error) { section.hidden = false; notice.textContent = error.message; }
        for (const control of section.querySelectorAll('button, input, select, textarea')) control.disabled = busy;
    }
    async function run(action) {
        if (busy || disposed || !host.isConnected || !app.isConnected) return;
        let captured;
        try {
            captured = session.context(); busy = true; update();
            await action(() => active(captured));
        } catch (error) { if (captured && active(captured)) status.textContent = error.message || 'The publication action failed.'; }
        finally { busy = false; if (!disposed) update(); }
    }
    form.addEventListener('submit', event => {
        event.preventDefault();
        if (!form.reportValidity() || !hasReview) return;
        void run(() => session.submit({ intent: { route: route.value.trim(), visibility: audience.value },
            ...(warning.value.trim() ? { warningAcknowledgement: warning.value.trim() } : {}) }));
    });
    retry.addEventListener('click', () => void run(() => session.retry()));
    acknowledge.addEventListener('click', () => void run(() => { session.acknowledge(); hasReview = false; }));
    cancel.addEventListener('click', () => { if (!busy) { hasReview = false; update(); } });
    app.addEventListener('honua-workspace-draft-change', update);
    update();
    return {
        review(versionId) {
            return run(async current => {
                const version = await session.review(versionId);
                if (!current()) return;
                hasReview = true;
                summary.textContent = `Publish saved version ${version.versionNumber} of ${version.packageKey}. Later draft edits are not included.`;
                route.value = version.envelope?.publicationIntent?.route || '';
                audience.value = audiences.has(version.envelope?.publicationIntent?.visibility) ? version.envelope.publicationIntent.visibility : 'personal';
                warning.value = ''; warningField.hidden = version.validation?.status !== 'warning';
                warning.required = !warningField.hidden;
                diagnostics.replaceChildren();
                for (const diagnostic of version.validation?.diagnostics || []) {
                    if (typeof diagnostic.message !== 'string') continue;
                    const row = document.createElement('li'); row.textContent = diagnostic.message; diagnostics.append(row);
                }
                status.textContent = 'Review the saved version, route and audience before publishing.';
            });
        },
        dispose() { disposed = true; app.removeEventListener('honua-workspace-draft-change', update); section.remove(); }
    };
}
