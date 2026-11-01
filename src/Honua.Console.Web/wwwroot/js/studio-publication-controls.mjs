import { capturedProcessFetch } from './cookie-workspace-transport.mjs';
import { publishedMapPath } from './published-map-viewer.mjs';

const id = value => typeof value === 'string' && value.length > 0 && value.length <= 128 && !/[^A-Za-z0-9_-]/.test(value);
const audiences = new Set(['personal', 'team', 'organization', 'public']);
const terminal = receipt => ['accepted', 'rejected', 'approval-rejected', 'approval-failed', 'approval-cancelled', 'approval-rolled-back'].includes(receipt?.status);

/** Retains submission identity before dispatch, including across a document reload. No credentials are stored. */
export class StudioPublicationSession {
    constructor(app, { storage = app.workspace?.storage, uuid = () => crypto.randomUUID(), readProposal } = {}) {
        this.app = app; this.storage = storage; this.uuid = uuid;
        this.readProposal = readProposal ?? (async proposalId => {
            const request = capturedProcessFetch(app.workspaceCookieBinding, { current: () => app.workspaceCookieBinding });
            const response = await request(`/api/processes/admin/proposals/${encodeURIComponent(proposalId)}`);
            if (!response.ok) throw new Error(`Approval status could not be read (${response.status}). The original submission is retained.`);
            return response.json();
        });
    }
    authority() {
        const binding = this.app.workspaceCookieBinding;
        if (!binding?.principalId || !binding.environmentId) throw new Error('Reconnect Studio before publishing.');
        return JSON.stringify([binding.principalId, binding.environmentId, binding.sourcePathBase || '']);
    }
    key() {
        const draftId = this.app.workspaceDraftIdentity?.draftId;
        if (!id(draftId)) throw new Error('Select a map draft before publishing.');
        if (!this.storage) throw new Error('Connect a persistent workspace before publishing.');
        return `honua.studio.publication.${encodeURIComponent(this.context())}`;
    }
    // A conversation can change without changing the publication's owner/target/map.
    context() { return JSON.stringify([this.authority(), this.app.workspaceDraftIdentity?.draftId]); }
    viewContext() {
        const workspaceId = this.app.workspace?.workspaceId;
        if (workspaceId != null && !id(workspaceId)) throw new Error('The conversation workspace is invalid.');
        return JSON.stringify([this.context(), workspaceId ?? null]);
    }
    legacyEntries() {
        const prefix = 'honua.workspace.studio-publication.';
        if (!Number.isInteger(this.storage.length) || this.storage.length > 10000 || typeof this.storage.key !== 'function')
            throw new Error('Persistent browser storage is required to recover publication references.');
        const entries = [];
        for (let index = 0; index < this.storage.length; index++) {
            const key = this.storage.key(index);
            if (!key?.startsWith(prefix) || !id(key.slice(prefix.length))) continue;
            const raw = this.storage.getItem(key);
            if (!raw) continue;
            const record = this.decode(raw);
            let context;
            try { context = JSON.parse(record.context); } catch { throw new Error('The original publication context is unreadable.'); }
            if (!Array.isArray(context) || context.length !== 3 || context[0] !== key)
                throw new Error('The original publication context is invalid.');
            if (context[1] === this.authority() && context[2] === this.app.workspaceDraftIdentity?.draftId
                && record.draftId === context[2]) entries.push({ key, record: { ...record, context: this.context() } });
        }
        return entries;
    }
    read() {
        const raw = this.storage.getItem(this.key());
        if (raw) return this.decode(raw);
        const legacy = this.legacyEntries();
        const identities = new Set(legacy.map(({ record }) => JSON.stringify([
            record.commandId, record.itemId, record.versionId, record.contentHash, record.input
        ])));
        if (identities.size > 1) throw new Error('Multiple original publication references exist for this map. Resolve them before publishing again.');
        return legacy[0]?.record ?? null;
    }
    decode(raw) {
        if (!raw) return null;
        let record;
        try { if (raw.length > 16384) throw new Error(); record = JSON.parse(raw); } catch { throw new Error('The original publication reference is unreadable. Restore it before submitting another publication.'); }
        if (!record || record.schemaVersion !== 1 || !['draftId', 'itemId', 'versionId', 'commandId'].every(key => id(record[key]))
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
    audienceLinks() {
        const record = this.read();
        if (!record || !this.bound(record) || record.receipt?.status !== 'accepted') return undefined;
        const map = publishedMapPath(record.input.intent.route);
        return {map, definition: '/api/studio/viewer/published/' + map.slice('/maps/published/'.length)};
    }
    async review(versionId) {
        if (this.read()) throw new Error('Resolve the original publication before starting another.');
        const context = this.viewContext();
        const version = await this.app.reviewWorkspacePublication(versionId);
        if (context !== this.viewContext()) throw new Error('The selected map or session changed during review.');
        this.reviewed = { version, context };
        return version;
    }
    submit(input) {
        if (this.pending) return this.pending;
        return this.run(async () => {
            if (this.read()) throw new Error('Use the original submission to recover its outcome.');
            const reviewed = this.reviewed;
            if (!reviewed || reviewed.context !== this.viewContext()) throw new Error('Review this saved version before publishing.');
            const canonical = { intent: { route: input.intent?.route, visibility: input.intent?.visibility },
                ...(input.warningAcknowledgement ? { warningAcknowledgement: input.warningAcknowledgement } : {}) };
            if (!this.validInput(canonical)) throw new Error('Choose an audience and a local publication route beginning with /.');
            const version = reviewed.version;
            const record = { schemaVersion: 1, context: this.context(), draftId: this.app.workspaceDraftIdentity.draftId,
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
            const context = this.viewContext();
            const version = this.reviewed?.context === context && this.reviewed.version.versionId === record.versionId
                ? this.reviewed.version : await this.app.reviewWorkspacePublication(record.versionId);
            if (context !== this.viewContext() || !this.bound(record) || version.itemId !== record.itemId || version.contentHash !== record.contentHash || version.versionId !== record.versionId)
                throw new Error('The original saved version could not be verified. No new publication was submitted.');
            return this.dispatch(record, version);
        });
    }
    refresh() {
        if (this.pending) return this.pending;
        return this.run(async () => {
            const record = this.read();
            if (!record || !this.bound(record)) throw new Error('Restore the original map, account and environment before refreshing.');
            const current = () => {
                if (!this.bound(record) || this.read()?.commandId !== record.commandId)
                    throw new Error('The workspace changed. The original submission reference is retained for recovery.');
            };
            if (id(record.receipt?.requestId) && this.app.readWorkspacePublication) {
                try {
                    const receipt = await this.app.readWorkspacePublication(record.versionId, record.receipt.requestId);
                    current();
                    if (receipt?.itemId !== record.itemId || receipt.versionId !== record.versionId
                        || receipt.requestId !== record.receipt.requestId || !['accepted', 'pending', 'rejected'].includes(receipt.status))
                        throw new Error('The publication status does not match the original submission.');
                    this.storage.setItem(this.key(), JSON.stringify({ ...record, receipt: {
                        ...record.receipt, status: receipt.status === 'pending' ? 'publication-pending' : receipt.status,
                    } }));
                    return receipt;
                } catch (error) {
                    current();
                    // The publication row does not exist until the approved actuator runs.
                    // A missing row never proves either rejection or successful publication.
                    if (error?.status !== 404) throw error;
                }
            }
            if (!id(record.receipt?.proposalId))
                throw new Error('No readable publication or approval reference was returned. Recover the original submission before trying another.');
            const proposal = await this.readProposal(record.receipt.proposalId);
            current();
            if (proposal?.proposalId !== record.receipt.proposalId || typeof proposal.status !== 'string')
                throw new Error('The approval status does not match the original submission.');
            const statuses = { planned: 'awaiting-approval', awaitingapproval: 'awaiting-approval',
                executing: 'approval-executing', submitted: 'approval-executing', reconciling: 'approval-executing',
                succeeded: 'approval-completed', completed: 'approval-completed', rejected: 'approval-rejected', failed: 'approval-failed',
                cancelled: 'approval-cancelled', rolledback: 'approval-rolled-back' };
            const statusKey = proposal.status.toLowerCase().replaceAll('-', '');
            const status = Object.hasOwn(statuses, statusKey) ? statuses[statusKey] : undefined;
            if (!status) throw new Error('The approval outcome is unknown. The original submission is retained.');
            this.storage.setItem(this.key(), JSON.stringify({ ...record, receipt: { ...record.receipt, status } }));
            return { status, proposalId: proposal.proposalId };
        });
    }
    async dispatch(record, version) {
        const key = this.key();
        const receipt = await this.app.publishWorkspaceVersion(version, record.input, record.commandId);
        if (!this.bound(record) || this.read()?.commandId !== record.commandId)
            throw new Error('The workspace changed. The original submission reference is retained for recovery.');
        const pending = receipt?.status === 'awaiting-approval' && id(receipt.operationInstanceId) && id(receipt.proposalId);
        const completed = ['accepted', 'rejected'].includes(receipt?.status) && receipt.itemId === record.itemId && receipt.versionId === record.versionId && id(receipt.requestId);
        if (pending && [receipt.itemId, receipt.versionId, receipt.requestId].some(value => value !== undefined)
            && (receipt.itemId !== record.itemId || receipt.versionId !== record.versionId || !id(receipt.requestId)))
            throw new Error('The publication outcome is unknown. Its resource references do not match the original submission.');
        if (!pending && !completed) throw new Error('The publication outcome is unknown. Retain and retry the original submission.');
        const retained = { status: receipt.status, ...(pending
            ? { operationInstanceId: receipt.operationInstanceId, proposalId: receipt.proposalId,
                ...(id(receipt.requestId) ? { requestId: receipt.requestId } : {}) }
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
        for (const { key, record: original } of this.legacyEntries())
            if (original.commandId === record.commandId) this.storage.removeItem(key);
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
    const refresh = document.createElement('button'); refresh.type = 'button'; refresh.textContent = 'Refresh publication status';
    const acknowledge = document.createElement('button'); acknowledge.type = 'button'; acknowledge.textContent = 'Acknowledge publication outcome';
    const inbox = document.createElement('a'); inbox.href = '/inbox'; inbox.textContent = 'Review approvals';
    const openMap = document.createElement('a'); openMap.textContent = 'Open published map';
    const definition = document.createElement('a'); definition.textContent = 'View map definition (JSON)';
    for (const link of [openMap, definition]) { link.target = '_blank'; link.rel = 'noopener noreferrer'; }
    const audienceActions = document.createElement('div'); audienceActions.className = 'console-action-row';
    audienceActions.append(openMap, definition);
    const cancel = document.createElement('button'); cancel.type = 'button'; cancel.textContent = 'Cancel publication review';
    for (const field of [route, audience, warning]) field.className = 'console-input';
    for (const label of [routeField, audienceField, warningField]) label.className = 'publish-field';
    for (const button of [publish, retry, refresh, acknowledge, cancel]) button.className = 'console-button console-button-secondary';
    publish.className = 'console-button';
    const fields = document.createElement('div'); fields.className = 'publish-field-grid'; fields.append(routeField, audienceField);
    const actions = document.createElement('div'); actions.className = 'console-action-row'; actions.append(publish, cancel);
    form.append(summary, fields, diagnostics, warningField, actions);
    section.append(notice, form, refresh, retry, acknowledge, inbox, audienceActions);
    const canvas = host.querySelector('[data-studio-mount]');
    if (canvas) canvas.before(section); else host.append(section);
    let busy = false, disposed = false, selectedContext, hasReview = false;
    const active = captured => {
        try { return !disposed && host.isConnected && app.isConnected && session.viewContext() === captured; }
        catch { return false; }
    };
    function update() {
        if (disposed) return;
        form.hidden = retry.hidden = refresh.hidden = acknowledge.hidden = inbox.hidden = openMap.hidden = definition.hidden = true;
        openMap.removeAttribute('href'); definition.removeAttribute('href');
        if (!id(app.workspaceDraftIdentity?.draftId) || !['reviewWorkspacePublication', 'publishWorkspaceVersion', 'readWorkspacePublication']
            .every(name => typeof app[name] === 'function')) {
            section.hidden = true; hasReview = false; selectedContext = undefined; return;
        }
        try {
            const context = session.viewContext(), record = session.read();
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
                        : record.receipt?.status === 'approval-rejected' ? 'A human approver rejected the publication.'
                        : record.receipt?.status === 'approval-failed' ? 'Approval execution failed. Inspect the approval and publication before submitting again.'
                        : record.receipt?.status === 'approval-cancelled' ? 'The approval was cancelled.'
                        : record.receipt?.status === 'approval-rolled-back' ? 'The approved operation was rolled back.'
                        : record.receipt?.status === 'approval-completed' ? (id(record.receipt.requestId)
                            ? 'Approval completed. Publication has not yet been verified; refresh its status.'
                            : 'Approval completed. Recover its publication receipt with the original submission.')
                        : ['approval-executing', 'publication-pending'].includes(record.receipt?.status) ? 'Publication is processing. Refresh its status for a confirmed outcome.'
                        : 'The submission outcome is unknown. Its original command and settings are retained.';
                    const canRefresh = id(record.receipt?.requestId) || id(record.receipt?.proposalId);
                    refresh.hidden = terminal(record.receipt) || !canRefresh;
                    retry.hidden = terminal(record.receipt) || canRefresh && !(record.receipt?.status === 'approval-completed' && !id(record.receipt.requestId));
                    acknowledge.hidden = !terminal(record.receipt);
                    inbox.hidden = !id(record.receipt?.proposalId);
                    if (!inbox.hidden) inbox.href = `/inbox?proposalId=${encodeURIComponent(record.receipt.proposalId)}`;
                    const links = session.audienceLinks();
                    if (links) {
                        openMap.href = links.map; definition.href = links.definition;
                        openMap.hidden = definition.hidden = false;
                    }
                }
            } else form.hidden = !hasReview;
        } catch (error) { section.hidden = false; notice.textContent = error.message; }
        for (const control of section.querySelectorAll('button, input, select, textarea')) control.disabled = busy;
    }
    async function run(action) {
        if (busy || disposed || !host.isConnected || !app.isConnected) return;
        let captured;
        try {
            captured = session.viewContext(); busy = true; update();
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
    refresh.addEventListener('click', () => void run(() => session.refresh()));
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
