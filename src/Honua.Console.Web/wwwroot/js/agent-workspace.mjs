import {GaSpecReview, reviewSummary, reviewDetails} from './ga-spec-review.mjs';
import {browserImportSelection, browserSelectionScope} from './dataset-import-selection.mjs';
import {PublicationRecovery} from './publication-recovery.mjs';
import {createPublishInputBinder} from './publish-form-inputs.mjs';
import { readSharedStyleFocus, prepareSharedStyleFocus, watchSharedStyleEdits } from './shared-style-context.mjs';
import { promptWithStudioDesignFocus, studioDesignFocusLabel } from './studio-design-focus.mjs';
// The workspace is shared by chat, forms and Studio. Each tab has its own explicit view binding.
const publishFields = new Set(['connectionId', 'schema', 'table', 'serviceName', 'layerName']);
export class WorkspaceConflict extends Error {
    constructor(current) { super('The shared draft changed. Review the latest version before saving.'); this.current = current; }
}
export class AgentWorkspace {
    constructor({ request = fetch, storage = sessionStorage, uuid = () => crypto.randomUUID(), notify = () => {} } = {}) {
        this.request = request; this.storage = storage; this.uuid = uuid; this.notify = notify;
        // New document means a new view even when the browser duplicates sessionStorage into a tab.
        this.viewId = uuid();
        storage.setItem('honua.workspace.view', this.viewId);
        this.workspaceId = storage.getItem('honua.workspace.id');
        this.context = null; this.session = null; this.deployTargetId = null; this.dirty = new Map(); this.follow = true;
        this.presentationGuards = new Set();
        this.queue = Promise.resolve(); this.agentSession = null; this.contextGeneration = 0;
        this.publications = new PublicationRecovery(this);
    }
    async api(path, method = 'GET', body, binding = this.session) {
        const response = await this.request(`/api/agent-workspace/${path}`, {
            method, credentials: 'same-origin', headers: {
                'Content-Type': 'application/json', 'X-CSRF-Token': binding?.csrfToken || '',
                'X-Honua-Environment': binding?.environmentId || '',
                'X-Honua-Session-Partition': binding?.principalId || '',
                ...(path.startsWith('ga/') ? {'X-Honua-Workspace':this.workspaceId || '', 'X-Honua-View':this.viewId} : {})
            }, ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const value = response.status === 204 ? null : await response.json().catch(() => null);
        if (!response.ok) {
            const error = response.status === 409 ? new WorkspaceConflict(value) : new Error(value?.message || (path.startsWith('ga/') ? [value?.code, value?.detail || value?.title].filter(Boolean).join(': ') : '') || `Workspace unavailable (${response.status}).`);
            error.status = response.status;
            error.notDispatched = response.headers?.get('X-Honua-Dispatch-Status') === 'not-dispatched';
            throw error;
        }
        return value;
    }
    async create() {
        if (this.capturing) throw new Error('Wait until the current context capture finishes.');
        const generation=++this.contextGeneration;
        const check=()=>{
            if(generation!==this.contextGeneration) throw new Error('The selected context changed while creating a workspace. Review it before retrying.');
            const style=this.sharedStyleProvider?.();
            if(this.hasUnsavedWork() || style?.dirty || style?.pending) throw new Error('Save or discard your edits before starting another workspace.');
        };
        check();await this.authorityChangeGuard?.();check();
        const candidate = await this.api('session');check();
        const context = await this.api('', 'POST', { viewId: this.viewId }, candidate);check();
        return this.connect(context.workspaceId);
    }
    async connect(workspaceId = this.workspaceId) {
        if (this.capturing) throw new Error('Wait until the submitted prompt has been bound before reconnecting.');
        const generation = ++this.contextGeneration;
        if (!workspaceId || !/^[A-Za-z0-9_-]{1,128}$/.test(workspaceId)) throw new Error('Connect a valid workspace shared with your agent.');
        const candidate = await this.api('session');
        const changedAuthority = Boolean(this.session && ['environmentId','principalId','releaseId','sourcePathBase'].some(key => this.session[key] !== candidate[key]));
        const changedWorkspace = this.workspaceId !== workspaceId;
        if (changedAuthority || changedWorkspace) {
            if (this.hasUnsavedWork()) throw new Error('Save or pause your work before changing workspace authority.');
            const style = this.sharedStyleProvider?.();
            if (style?.dirty || style?.pending) throw new Error('Save or discard the style edit before changing connections.');
            await this.authorityChangeGuard?.();
        }
        const checkAdoption = async () => {
            if (generation !== this.contextGeneration) throw new Error('The selected context changed while connecting. Review it before retrying.');
            if (changedAuthority || changedWorkspace) {
                if (this.hasUnsavedWork()) throw new Error('Save or pause your work before changing workspace authority.');
                const style = this.sharedStyleProvider?.();
                if (style?.dirty || style?.pending) throw new Error('Save or discard the style edit before changing connections.');
                await this.authorityChangeGuard?.();
                const latestStyle=this.sharedStyleProvider?.();
                if (generation !== this.contextGeneration || this.hasUnsavedWork() || latestStyle?.dirty || latestStyle?.pending)
                    throw new Error('The editor changed while connecting. Save it before retrying.');
            }
        };
        // Adopt nothing until the new cookie/profile binding has actually authorized this view.
        const context = await this.api(`${workspaceId}?viewId=${encodeURIComponent(this.viewId)}`, 'GET', undefined, candidate);
        await checkAdoption();
        if (changedAuthority || changedWorkspace) {
            this.agentSession?.sdkSession?.close(); this.agentSession = null; this.draft = null; this.draftFields = null; this.presentedDraft = null;
            this.deployTargetId = null;
        }
        this.session = candidate; this.workspaceId = workspaceId; this.context = context;
        this.storage.setItem('honua.workspace.id', workspaceId);
        await attachNativeAgent(this).catch(error => { if(generation===this.contextGeneration) this.nativeAgentIssue=error.message; });
        if(generation!==this.contextGeneration) throw new Error('A newer workspace connection replaced this request.');
        await this.agentSession?.reconcilePending?.();
        this.notify(this); return this.context;
    }
    async contextReference() {
        if (!this.context || !this.session) throw new Error('Start or connect a workspace first.');
        const binding = {generation:this.contextGeneration,workspaceId:this.workspaceId,viewId:this.viewId,
            principalId:this.session.principalId,environmentId:this.session.environmentId};
        const check = () => {
            if (binding.generation !== this.contextGeneration || binding.workspaceId !== this.workspaceId
                || binding.viewId !== this.viewId || binding.principalId !== this.session?.principalId
                || binding.environmentId !== this.session?.environmentId)
                throw new Error('The selected context changed. Copy the context again when ready.');
        };
        this.capturing = (this.capturing || 0) + 1;
        try {
            await this.queue; check();
            if (this.dirty.size) throw new Error('Save your draft before copying its context.');
            const style = this.sharedStyleProvider?.();
            const prepare = style ? async()=>prepareSharedStyleFocus(style, this.session?.principalId) : this.prepareContextProvider?.();
            const task = prepare ? await prepare() : undefined;
            check();
            if (task !== undefined && JSON.stringify(task) !== JSON.stringify(this.context.task))
                throw new Error('Wait for the accepted task context before copying it.');
            if (this.dirty.size) throw new Error('Save your draft before copying its context.');
            const observed = await this.api(`${this.workspaceId}?viewId=${encodeURIComponent(this.viewId)}`);
            check();
            if (observed.revision !== this.context.revision || observed.targetId !== this.context.targetId)
                throw new Error('The server context changed. Review it before copying a new reference.');
            return Object.freeze({workspaceId:this.workspaceId,viewId:this.viewId,contextRevision:observed.revision,targetId:observed.targetId,
                ...(this.deployTargetId ? {deployTargetId:this.deployTargetId} : {})});
        } finally { this.capturing--; }
    }
    async observeContext() {
        const settled = this.queue;
        await settled;
        const snapshot = await this.api(`${this.workspaceId}?viewId=${this.viewId}`);
        if (this.queue !== settled || this.capturing || snapshot.revision < (this.context?.revision ?? -1)) return undefined;
        this.context = snapshot;
        return snapshot;
    }
    holdSelectionSource() {
        // The clear control suppresses this provider snapshot. Submit must not restore it.
        this.selectionSourceHold = JSON.stringify(this.selectionProvider?.() ?? null);
    }
    promptSelection() {
        if (this.selectionSourceHold !== undefined) {
            const live = JSON.stringify(this.selectionProvider?.() ?? null);
            if (live === this.selectionSourceHold) return undefined;
            this.selectionSourceHold = undefined;
        }
        return this.selectionProvider?.();
    }
    select(resources, task) {
        this.contextGeneration++;
        // Serialize edits using authorized server revisions, never client timestamps or event ordering.
        const update = async () => {
            if (!this.context) return;
            const selectedTask = task === undefined ? this.context.task : task;
            const selectedResources = resources === undefined ? this.context.selectedResources : resources;
            if (JSON.stringify(this.context.selectedResources || []) === JSON.stringify(selectedResources)
                && JSON.stringify(this.context.task) === JSON.stringify(selectedTask)) return this.context;
            this.context = await this.api(`${this.workspaceId}/context`, 'PUT', {
                expectedRevision: this.context.revision, viewId: this.viewId,
                targetId: this.context.targetId, selectedResources, task: selectedTask, source: 'console'
            });
            this.notify(this); return this.context;
        };
        this.queue = this.queue.then(update, update); return this.queue;
    }
    edit(field, value) {
        if (!publishFields.has(field))
            throw new Error('This field is not part of the shared publish-layer draft.');
        this.dirty.set(field, value); this.publications.persistCandidate(); this.notify(this);
    }
    async reconcileDraft(draft) {
        // Local field candidates belong to one draft; another task cannot inherit them.
        if (this.dirty.size && draft?.id !== this.draft?.id)
            throw new Error('Another publish draft is selected. Return to your original form and save or discard its unsaved edits.');
        // A newer observation of that same draft preserves local unsaved fields.
        this.draft = draft; this.notify(this); return this.projectDraft();
    }
    projectDraft() { return { ...this.draft?.fields, ...Object.fromEntries(this.dirty) }; }
    focusPublishDraft(isCurrentView = () => true) {
        const generation = ++this.contextGeneration;
        const binding = {workspaceId:this.workspaceId,viewId:this.viewId,authority:this.authorityKey()};
        const sameAuthority = () => binding.workspaceId === this.workspaceId && binding.viewId === this.viewId
            && binding.authority === this.authorityKey();
        const current = () => sameAuthority() && generation === this.contextGeneration && isCurrentView();
        const update = async () => {
            if (!this.context || !current()) return false;
            const revision = this.context.revision;
            const cachedDraftId = this.draft?.id;
            const draftId = cachedDraftId || this.publications.draftReference();
            const draft = draftId ? await this.api(`${this.workspaceId}/drafts/${encodeURIComponent(draftId)}`) : null;
            if (!current() || revision !== this.context.revision || cachedDraftId !== this.draft?.id) return false;
            if (draft && (draft.kind !== 'publish-layer' || draft.id !== draftId))
                throw new Error('The publish draft changed. Review the current form before continuing.');
            const task = draft ? {id:draft.id,revision:draft.revision,kind:'publish-layer'} : null;
            const context = await this.api(`${this.workspaceId}/context`, 'PUT', {
                expectedRevision:revision,viewId:this.viewId,targetId:this.context.targetId,
                selectedResources:[],task,source:'console'
            });
            // Later queued navigation needs this accepted CAS revision; another authority must never adopt it.
            if (!sameAuthority()) return false;
            if (draft) { this.draft = draft; this.publications.restoreCandidate(draft); }
            this.context = context; this.notify(this);
            return current();
        };
        this.queue = this.queue.then(update, update); return this.queue;
    }
    authorityKey() {
        return JSON.stringify(['principalId','environmentId','releaseId','sourcePathBase'].map(key => this.session?.[key]));
    }
    async ensureDraft(fields, isCurrent = () => true) {
        if (!isCurrent()) throw new Error('The publish form changed. Review it before saving.');
        if (this.draft) return this.draft;
        await this.publications.requireResolved(isCurrent);
        const workspaceId = this.workspaceId, viewId = this.viewId, authority = this.authorityKey();
        const current = () => isCurrent() && this.workspaceId === workspaceId && this.viewId === viewId
            && this.authorityKey() === authority && !this.draft;
        const draft = await this.api(`${workspaceId}/drafts`, 'POST', {
            kind:'publish-layer',fields,viewId,contextRevision:this.context.revision
        });
        if (!current()) throw new Error('The publish form changed while creating its draft. Review the original workspace before retrying.');
        const context = await this.api(`${workspaceId}?viewId=${viewId}`);
        if (!current()) throw new Error('The publish form changed while reading its draft. Review the original workspace before retrying.');
        this.draft = draft; this.context = context;
        return draft;
    }
    async saveDraft(submitted = new Map(this.dirty), isCurrent = () => true) {
        if (!submitted.size) return this.draft;
        if (!isCurrent()) throw new Error('The publish form changed. Review it before saving.');
        const initialFields = !this.draft ? {...this.draftFields?.(),...Object.fromEntries(submitted)} : null;
        const originalAuthority = this.authorityKey(), originalWorkspace = this.workspaceId, originalView = this.viewId;
        const originalCurrent = () => isCurrent() && this.authorityKey() === originalAuthority
            && this.workspaceId === originalWorkspace && this.viewId === originalView;
        await this.publications.requireResolved(originalCurrent);
        if (!originalCurrent()) throw new Error('The publish connection changed. Review it before saving.');
        if (!this.draft) await this.ensureDraft(initialFields, isCurrent);
        if (!isCurrent()) throw new Error('The publish form changed. Review its canonical draft before retrying.');
        const workspaceId = this.workspaceId, viewId = this.viewId, authority = this.authorityKey();
        const draftId = this.draft.id, revision = this.draft.revision;
        const current = () => isCurrent() && this.workspaceId === workspaceId && this.viewId === viewId
            && this.authorityKey() === authority && this.draft?.id === draftId && this.draft?.revision === revision;
        const result = await this.api(`${workspaceId}/drafts/${encodeURIComponent(draftId)}`, 'PATCH', {
            expectedRevision:revision,viewId,changes:Object.fromEntries(submitted)
        });
        if (!current()) throw new Error('The original draft save may have been accepted. Review it before retrying.');
        for (const [field, value] of submitted) if (this.dirty.get(field) === value) this.dirty.delete(field);
        this.draft = result; this.notify(this); return result;
    }
    async submitDraft({save = true, isCurrent = () => true} = {}) {
        // Pending identity is independent of the mutable draft revision, including after reload.
        if (this.publications.read()) {
            const receipt = await this.publications.reconcile(isCurrent);
            return {data:receipt};
        }
        if (!this.draft) throw new Error('No shared draft selected.');
        if (save) await this.saveDraft(undefined, isCurrent);
        if (!isCurrent()) throw new Error('The publish form changed. Review the original command before retrying.');
        const workspaceId = this.workspaceId, viewId = this.viewId;
        const draftId = this.draft.id, revision = this.draft.revision;
        const key = this.publications.commandKey(this.draft);
        const existing = this.storage.getItem(key);
        const commandId = existing || this.uuid();
        const record = this.publications.create(commandId, this.draft);
        if (existing) {
            const receipt = await this.publications.reconcile(isCurrent);
            return {data:receipt};
        }
        this.storage.setItem(key, commandId);
        const receipt = await this.api(`${workspaceId}/drafts/${draftId}/submit`, 'POST', {
            commandId,expectedRevision:revision,viewId
        });
        this.publications.observe(record,receipt);
        if (!isCurrent()) throw new Error('The original publication may have been submitted. Review its recorded command before retrying.');
        return receipt;
    }
    async savePublishForm(fields, {publish = false, isCurrentView = () => true} = {}) {
        // This is the mounted Save/Publish click boundary, before any asynchronous focus or creation.
        const submitted = new Map(this.dirty);
        const snapshot = Object.fromEntries(Object.entries({...fields,...Object.fromEntries(submitted)})
            .filter(([field]) => publishFields.has(field)));
        const workspaceId = this.workspaceId, viewId = this.viewId, authority = this.authorityKey();
        let draftId = this.draft?.id;
        let draftRevision = this.draft?.revision;
        let generation = this.contextGeneration;
        const current = () => isCurrentView() && this.workspaceId === workspaceId && this.viewId === viewId
            && this.authorityKey() === authority && this.contextGeneration === generation
            && this.draft?.id === draftId && this.draft?.revision === draftRevision;
        const requireCurrent = () => { if (!current()) throw new Error('The selected form or connection changed. Your original fields are retained; review before retrying.'); };
        requireCurrent();
        await this.publications.requireResolved(current);
        requireCurrent();
        const focus = this.focusPublishDraft(() => isCurrentView() && this.workspaceId === workspaceId
            && this.viewId === viewId && this.authorityKey() === authority
            && this.draft?.id === draftId && this.draft?.revision === draftRevision);
        generation = this.contextGeneration;
        if (!await focus) throw new Error('The selected task changed. Your original fields are retained; review before retrying.');
        requireCurrent();
        const draft = await this.ensureDraft(snapshot, current);
        if (draftId === undefined) { draftId = draft.id; draftRevision = draft.revision; }
        requireCurrent();
        if ((this.context.task?.kind || 'publish-layer') !== 'publish-layer' || this.context.task?.id !== draftId)
            throw new Error('The canonical task changed. Review the original publish draft before saving.');
        const saved = await this.saveDraft(submitted, current);
        draftRevision = saved.revision;
        requireCurrent();
        const revision = this.draft.revision;
        const observed = await this.api(`${workspaceId}?viewId=${encodeURIComponent(viewId)}`);
        requireCurrent();
        if (observed.task?.id !== draftId || observed.task?.revision !== revision || (observed.task?.kind || 'publish-layer') !== 'publish-layer')
            throw new Error('The canonical draft changed after saving. Review it before submitting a publication.');
        this.context = observed; this.notify(this);
        if (publish) return this.submitDraft({save:false,isCurrent:current});
        return this.draft;
    }
    assertPublishEditsCurrent(allowPendingFormFocus = false) {
        if (!this.dirty.size) return;
        const refuse = () => { throw new Error('Return to the publish form and save or discard its unsaved edits before sending from another task.'); };
        if (this.publishViewCurrent) {
            if (!this.publishViewCurrent()) refuse();
            // The route-owned focus is already queued ahead of capture and will be checked again after it settles.
            if (allowPendingFormFocus) return;
        }
        const task = this.context?.task;
        if (task && ((task.kind || 'publish-layer') !== 'publish-layer' || this.draft && task.id !== this.draft.id)) refuse();
    }
    async submit(prompt, options = {}) {
        if (!prompt.trim()) throw new Error('Enter a message.');
        const browserFile = browserImportSelection.forTurn(this);
        this.assertPublishEditsCurrent(true);
        const styleFocus = this.sharedStyleProvider?.();
        const designFocus = styleFocus ? undefined : this.studioDesignFocusProvider?.();
        let capturedPrompt = prompt;
        const selection = styleFocus ? [] : this.promptSelection();
        const capturedResources = selection ?? this.context?.selectedResources ?? [];
        const prepareContext = options.prepareContext || (styleFocus ? async () => prepareSharedStyleFocus(styleFocus, this.session?.principalId) : undefined) || this.prepareContextProvider?.()
            || (typeof location !== 'undefined' && location.pathname === '/studio/processes' ? async()=>null : undefined);
        if (selection && JSON.stringify(selection) !== JSON.stringify(this.context?.selectedResources)) this.select(selection);
        // Enqueue capture synchronously. Navigation after this call queues behind the durable turn
        // creation, but remains free to change the visible page while the model runs.
        const binding = { generation: this.contextGeneration, workspaceId: this.workspaceId, viewId: this.viewId,
            principalId: this.session?.principalId, environmentId: this.session?.environmentId,
            deployTargetId: this.deployTargetId };
        this.capturing = (this.capturing || 0) + 1;
        const requireSameAuthority = () => {
            if (browserFile && JSON.stringify(browserSelectionScope(this)) !== JSON.stringify(browserFile.scope))
                throw new Error('The selected file belongs to an earlier context. Check it again before sending.');
            if (binding.workspaceId !== this.workspaceId || binding.viewId !== this.viewId
                || binding.principalId !== this.session?.principalId || binding.environmentId !== this.session?.environmentId
                || binding.deployTargetId !== this.deployTargetId)
                throw new Error('The workspace authority changed while submitting. Reconnect before retrying.');
        };
        const previous = this.queue;
        const capture = async () => {
            await previous;
            requireSameAuthority();
            if (!this.context) throw new Error('Connect a workspace first.');
            if (prepareContext) {
                const task = await prepareContext();
                requireSameAuthority();
                if (task !== undefined) {
                    const sameTask = this.context.task?.id === task?.id && this.context.task?.kind === task?.kind;
                    const acceptedLocalSave = !this.context.task && task?.kind === 'local-process-workspace'
                        && binding.generation === this.contextGeneration && binding.workspaceId === this.workspaceId
                        && binding.viewId === this.viewId && binding.principalId === this.session?.principalId
                        && binding.environmentId === this.session?.environmentId;
                    if (task && !sameTask && !acceptedLocalSave)
                        throw new Error('The workspace task changed while submitting. Review the current task before retrying.');
                    this.context = await this.api(`${this.workspaceId}/context`, 'PUT', {
                        expectedRevision: this.context.revision, viewId: this.viewId, targetId: this.context.targetId,
                        selectedResources: capturedResources, task, source: 'console'
                    });
                }
            }
            if (this.dirty.size) {
                this.assertPublishEditsCurrent();
                await this.saveDraft();
                this.context = await this.api(`${this.workspaceId}?viewId=${this.viewId}`);
            }
            requireSameAuthority();
            if (browserFile && (this.context.revision !== browserFile.scope.contextRevision || this.context.targetId !== browserFile.scope.targetId))
                throw new Error('The selected file belongs to an earlier context. Check it again before sending.');
            capturedPrompt = promptWithStudioDesignFocus(prompt, designFocus, this.context.task);
            return this.api(`${this.workspaceId}/turns`, 'POST', {
                turnId: this.uuid(), viewId: this.viewId, contextRevision: this.context.revision, prompt:capturedPrompt,
                ...(binding.deployTargetId ? {deployTargetId:binding.deployTargetId} : {}),
                ...(browserFile ? {browserFileSelection:browserFile.metadata} : {})
            });
        };
        const pending = capture();
        this.queue = pending.then(() => this.context, () => this.context);
        let turn;
        try { turn = await pending; browserImportSelection.captured(this, turn, browserFile); } finally { this.capturing--; }
        this.notify(this);
        if (turn.presentation && typeof window !== 'undefined')
            window.dispatchEvent(new CustomEvent('honua-agent-presentation', { detail: turn.presentation }));
        if (!this.agentSession) return { ...turn, executionUnavailable: true };
        return this.agentSession.submitTurn({ ...turn, prompt:capturedPrompt }, options);
    }
    async conversation() { return this.api(`${this.workspaceId}/conversation`); }
    hasUnsavedWork() { return this.dirty.size > 0 || [...this.presentationGuards].some(guard => guard()); }
    presentation(intent) {
        if (!this.follow || this.hasUnsavedWork()) return null;
        if (intent?.view === 'dataset-import' && this.context?.task?.kind === 'dataset-import') return '/operate/resources/import/task';
        if (intent?.view === 'studio') return '/studio';
        if (intent?.view === 'publish-layer') return '/operate/publishing/quick';
        if (intent?.view === 'layer' && /^[A-Za-z0-9_-]{1,128}$/.test(intent.resourceId))
            return `/operate/layers/${encodeURIComponent(intent.resourceId)}`;
        return null;
    }
}

export function selectedResource(pathname) {
    const match = /^\/operate\/layers\/([A-Za-z0-9_-]{1,128})(?:\/[^/?#]*)?$/.exec(pathname);
    if (!match) return [];
    // The Console's server mapper owns this canonical route format: connection-token-layer-ID.
    const physicalId = /(?:^|-layer-)([0-9]{1,10})$/.exec(match[1])?.[1];
    return physicalId ? [{ kind: 'layer', id: physicalId }] : [];
}

export function studioResourceRefs(targets, layers, catalog) {
    const resources = new Map();
    for (const target of Array.isArray(targets) ? targets.slice(0, 32) : []) {
        const sourceId = target.kind === 'layer' ? layers?.find(layer => layer.id === target.id)?.sourceId
            : target.kind === 'feature' ? target.sourceId : undefined;
        const locator = catalog?.find(source => source.id === sourceId)?.locator;
        if (!locator || !Number.isSafeInteger(locator.layerId) || locator.layerId < 0
            || typeof locator.serviceId !== 'string' || !locator.serviceId || locator.serviceId.length > 256) continue;
        const reference = { kind: 'layer', id: String(locator.layerId), serviceId: locator.serviceId };
        resources.set(JSON.stringify(reference), reference);
    }
    return [...resources.values()];
}

let activeWorkspace;
const mountedWorkspaceHosts = new WeakSet();
export function mountWorkspace() {
    const host = document.querySelector('[data-agent-workspace]');
    if (!host || mountedWorkspaceHosts.has(host)) return;
    mountedWorkspaceHosts.add(host);
    host.dataset.mounted = 'true';
    const lifetime = new AbortController();
    watchSharedStyleEdits(document, {signal:lifetime.signal});
    const status = host.querySelector('[data-workspace-status]');
    const messages = host.querySelector('[data-workspace-messages]');
    const target = host.querySelector('[data-workspace-target]');
    const selectionLabel = host.querySelector('[data-workspace-selection-label]');
    const clearSelection = host.querySelector('[data-workspace-clear-selection]');
    const objectName = resource => {
        const provided = [resource?.name, resource?.title, resource?.label].find(value => typeof value === 'string' && value.trim() && !/https?:|secret|token|password/i.test(value));
        const kind = resource?.kind === 'feature' ? 'Feature' : resource?.kind === 'component' ? 'Component' : resource?.kind === 'layer' ? 'Layer' : 'Object';
        if (resource?.unavailable || resource?.removed || resource?.stale) return `${kind} unavailable`;
        return provided ? `${kind} ${provided.trim()}` : kind;
    };
    const workspaceInput = host.querySelector('[data-workspace-id]');
    const targetDescription = document.createTextNode(''), taskSeparator = document.createTextNode('');
    const importTaskLink = document.createElement('a');
    importTaskLink.href = '/operate/resources/import/task'; importTaskLink.textContent = 'Open import task';
    target.replaceChildren(targetDescription, taskSeparator, importTaskLink);
    const notify = state => {
        const taskLabel = state.context?.task ? ({'studio-composition':'Map draft','publish-layer':'Publish layer','workflow-package':'Process graph','local-process-workspace':'Local process','shared-style':'Shared style','dataset-import':'Dataset import'}[state.context.task.kind] || 'Task') : 'No task selected';
        const taskIdentity = state.context?.task?.packageKey || state.context?.task?.id;
        const currentTask = taskIdentity ? `${taskLabel} ${taskIdentity}` : taskLabel;
        const resources = state.context?.selectedResources?.map(objectName).join(', ');
        const designLabel = state.context?.task?.kind === 'studio-composition'
            ? studioDesignFocusLabel(state.studioDesignFocusProvider) : undefined;
        const selectedCount = state.context?.selectedResources?.length || 0;
        const mapName = state.context?.task?.name || state.context?.task?.title || state.context?.task?.packageKey;
        if (selectionLabel) selectionLabel.textContent = state.context
            ? `${state.session?.environmentName || 'Environment'} · ${mapName ? `Map ${mapName}` : 'No map'} · ${designLabel || (selectedCount ? resources : 'No object selected')}`
            : 'No map or object selected.';
        if (clearSelection) clearSelection.hidden = !selectedCount && !designLabel;
        targetDescription.nodeValue = state.context ? (state.session?.gaSpecWorkflow
            ? `Next request · ${state.session.environmentName} · Editing target: ${currentTask} · Selection: ${resources || 'none'} · Context revision ${state.context.revision} · Draft revision ${state.context.task?.revision ?? 'none'}`
            : `${state.session.environmentName} · ${resources || 'No resource selected'} · ${currentTask}`) : 'Choose an environment, then start a conversation.';
        importTaskLink.hidden = state.context?.task?.kind !== 'dataset-import';
        taskSeparator.nodeValue = importTaskLink.hidden ? '' : ' · ';
        const header = document.querySelector('[data-workspace-header-target]');
        if (header) header.textContent = state.context ? `${state.session.environmentName} · ${resources || currentTask}` : 'Choose an environment';
        host.dataset.connected = String(Boolean(state.context));
        const start = host.querySelector('[data-workspace-start]');
        if (start) start.hidden = Boolean(state.context);
        host.classList.toggle('console-first-run-conversation', !state.context && location.pathname === '/');
        state.renderPublicationReview?.();
        const reviewPanel = host.querySelector('[data-ga-spec-review]');
        if (reviewPanel) reviewPanel.hidden = !state.session?.gaSpecWorkflow;
        window.dispatchEvent(new CustomEvent('honua-workspace-state'));
        target.title = state.context ? `Target ${state.context.targetId}; tenant ${state.context.tenantId ?? 'unbound'}; revision ${state.context.revision}` : '';

    };
    const workspace = activeWorkspace ||= new AgentWorkspace();
    const deployTargetSelect = host.querySelector('[data-workspace-deploy-target]');
    let targetCatalogBinding = null;
    const refreshDeployTargets = async state => {
        const binding = state.workspaceId && state.session
            ? `${state.workspaceId}\u001f${state.authorityKey()}` : null;
        if (binding === targetCatalogBinding) return;
        targetCatalogBinding = binding;
        const previous = state.deployTargetId;
        state.deployTargetId = null;
        if (deployTargetSelect) deployTargetSelect.replaceChildren(new Option('No deployment selected', ''));
        if (!binding || !deployTargetSelect) return;
        try {
            const targets = await state.api(`${state.workspaceId}/deploy-targets`);
            if (targetCatalogBinding !== binding || !Array.isArray(targets)) return;
            for (const target of targets) {
                if (typeof target.targetId !== 'string' || typeof target.targetName !== 'string' ||
                    typeof target.environment !== 'string') continue;
                deployTargetSelect.add(new Option(`${target.environment} · ${target.targetName}`, target.targetId));
            }
            if ([...deployTargetSelect.options].some(option => option.value === previous)) {
                deployTargetSelect.value = previous; state.deployTargetId = previous;
            }
        } catch (error) {
            if (targetCatalogBinding === binding) { targetCatalogBinding = null; status.textContent = error.message; }
        }
    };
    deployTargetSelect?.addEventListener('change', () => {
        workspace.deployTargetId = deployTargetSelect.value || null;
        workspace.contextGeneration++;
        notify(workspace);
    }, {signal:lifetime.signal});
    workspace.notify = state => { notify(state); void refreshDeployTargets(state); };
    workspace.notify(workspace);
    workspace.open ??= window.matchMedia('(min-width: 1100px)').matches;
    host.open = workspace.open;
    const connectionPanel = host.querySelector('.console-conversation-connection');
    if (connectionPanel) connectionPanel.open = false;
    host.addEventListener('toggle', () => { workspace.open = host.open; }, { signal: lifetime.signal });
    workspace.sharedStyleProvider ||= () => readSharedStyleFocus(document, workspace.session?.principalId);
    workspace.selectionProvider ||= () => selectedResource(location.pathname);
    // One session instance lives above route components; embedded Studio receives this same object.
    Object.defineProperty(window, 'honuaAgentWorkspace', { value: workspace, configurable: true });
    const report = error => { status.textContent = error.message; };
    const review = new GaSpecReview(workspace);
    const renderReview = () => {
        host.querySelector('[data-ga-plan-summary]').textContent = reviewSummary(review.plan, review.run);
        host.querySelector('[data-ga-plan-details]').textContent = reviewDetails(review.plan, review.run);
        host.querySelector('[data-ga-plan-apply]').disabled = !review.plan || Boolean(review.run)
            || review.plan.execution !== 'planned' || !['create','noop'].includes(review.plan.verdict);
    };
    for (const [selector, action] of [
        ['[data-ga-plan-current]', () => review.planCurrentDraft()],
        ['[data-ga-plan-open]', () => review.open(host.querySelector('[data-ga-plan-id]').value.trim(), host.querySelector('[data-ga-plan-revision]').value.trim())],
        ['[data-ga-plan-apply]', () => review.apply()],
        ['[data-ga-plan-refresh]', () => review.reconnect()]
    ]) host.querySelector(selector)?.addEventListener('click', async () => {
        try { await action(); renderReview(); } catch (error) { report(error); }
    }, {signal:lifetime.signal});
    const refresh = async () => {
        const value = await workspace.conversation();
        messages.replaceChildren();
        window.dispatchEvent(new CustomEvent('honua-workspace-conversation', { detail: value.messages || [] }));
        for (const message of value.messages || []) {
            const row = document.createElement('p');
            row.textContent = `${message.role}: ${message.content}`; messages.append(row);
        }
    };
    host.querySelector('[data-workspace-start]')?.addEventListener('click', async () => {
        try { await workspace.create(); workspaceInput.value = workspace.workspaceId; await workspace.select(workspace.selectionProvider?.() ?? selectedResource(location.pathname)); await refresh(); status.textContent = 'Connected'; }
        catch (error) { report(error); }
    });
    host.querySelector('[data-workspace-connect]').addEventListener('click', async () => {
        try { await (workspaceInput.value.trim() ? workspace.connect(workspaceInput.value.trim()) : workspace.create()); workspaceInput.value = workspace.workspaceId; await workspace.select(workspace.selectionProvider?.() ?? selectedResource(location.pathname)); await refresh(); status.textContent = 'Connected'; }
        catch (error) { report(error); }
    });
    host.querySelector('[data-workspace-copy-context]')?.addEventListener('click', async () => {
        try {
            const reference = await workspace.contextReference();
            const text = JSON.stringify(reference, null, 2);
            const output = host.querySelector('[data-workspace-context-reference]');
            if (output) { output.textContent = text; output.hidden = false; }
            try { await navigator.clipboard.writeText(text); status.textContent = 'Tab context copied. Give it to your agent with your next request.'; }
            catch { status.textContent = 'Copy the tab context shown below and give it to your agent. It contains no credentials.'; }
        } catch (error) { report(error); }
    });
    host.querySelector('[data-workspace-follow]').addEventListener('change', event => { workspace.follow = event.target.checked; });
    const working = host.querySelector('[data-workspace-working]');
    const cancelButton = host.querySelector('[data-workspace-cancel]');
    const cancelNote = host.querySelector('[data-workspace-cancel-note]');
    const partial = host.querySelector('[data-workspace-partial]');
    const sendButton = host.querySelector('form')?.querySelector('button[type=submit]');
    let turnAbort = null;
    const setTurnWorking = active => {
        if (working) working.hidden = !active;
        if (cancelButton) cancelButton.hidden = !active;
        if (cancelNote) cancelNote.hidden = !active;
        if (sendButton) sendButton.disabled = active;
        if (!active && partial) { partial.textContent = ''; partial.hidden = true; }
    };
    cancelButton?.addEventListener('click', () => { turnAbort?.abort(); }, { signal: lifetime.signal });
    host.querySelector('[data-workspace-change-selection]')?.addEventListener('click', () => {
        host.querySelector('[data-workspace-deploy-target]')?.focus();
    }, { signal: lifetime.signal });
    clearSelection?.addEventListener('click', () => {
        workspace.holdSelectionSource();
        workspace.clearStudioDesignFocus?.();
        workspace.select([]).catch(report);
        workspace.notify(workspace);
    }, { signal: lifetime.signal });
    host.querySelector('form').addEventListener('submit', async event => {
        event.preventDefault(); const input = host.querySelector('[data-workspace-prompt]');
        turnAbort = new AbortController();
        setTurnWorking(true);
        if (partial) { partial.textContent = ''; partial.hidden = true; }
        try {
            const submitted = input.value;
            status.textContent = `Submitting for target ${workspace.context?.targetId || 'unbound'} · ${workspace.context?.task?.id || 'no task'}.`;
            const result = await workspace.submit(submitted, {signal: turnAbort.signal, onEvent: event => {
                const inner = event?.event ?? event;
                if (inner?.type === 'textDelta' && typeof inner.text === 'string' && partial) {
                    partial.hidden = false;
                    partial.textContent += inner.text;
                }
                if (workspace.session?.gaSpecWorkflow) status.textContent = 'Agent is working on the submitted context. Operation completion requires durable read-back.';
            }}); if(input.value === submitted) input.value = ''; await refresh();
            const cancelled = turnAbort.signal.aborted || result.status === 'cancelled';
            status.textContent = result.executionUnavailable ? 'Message saved. Native agent session is not configured; external agents can continue this workspace.' : cancelled ? (result.errorMessage || 'Agent turn cancelled.') : ['error', 'refused', 'failed', 'blocked'].includes(result.status) ? (result.errorMessage || `Agent turn ${result.status}.`) : 'Turn saved. Check operation receipts for completion.';
        } catch (error) { report(error); }
        finally { setTurnWorking(false); turnAbort = null; }
    });
    // Navigation publishes only typed selections. Never forward a URL, query, fragment or page contents.
    let lastSelection = JSON.stringify(selectedResource(location.pathname));
    let hadSharedStyle = false;
    const observer = new MutationObserver(() => {
        if (!host.isConnected) { clearTimeout(workspace._pollTimer); observer.disconnect(); formObserver.disconnect(); lifetime.abort(); return; }
        const focus = workspace.sharedStyleProvider?.();
        const provided = focus ? undefined : workspace.promptSelection();
        const selection = focus ? [] : provided ?? (workspace.selectionSourceHold !== undefined ? (workspace.context?.selectedResources ?? []) : selectedResource(location.pathname));
        const key = JSON.stringify(focus ? {resources:selection,task:focus.task} : selection);
        if (workspace.context && key !== lastSelection) {
            lastSelection = key;
            workspace.select(selection, focus ? focus.task : hadSharedStyle ? null : undefined).catch(report);
        }
        hadSharedStyle = Boolean(focus);
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true,
        attributeFilter:['data-style-authority-binding','data-style-task-id','data-style-task-revision','data-style-content-hash','data-style-impact-hash'] });
    window.addEventListener('honua-agent-presentation', event => {
        const href = workspace.presentation(event.detail);
        if (href === location.pathname) return;
        if (href) { if (window.Blazor?.navigateTo) window.Blazor.navigateTo(href); else { const link = document.createElement('a'); link.href = href; link.textContent = 'Open proposed task'; status.replaceChildren(link); } }
        else status.textContent = 'Proposed view paused. Save unsaved edits or enable follow navigation to continue.';
    }, { signal: lifetime.signal });
    window.addEventListener('beforeunload', event => { if (workspace.hasUnsavedWork()) { event.preventDefault(); event.returnValue = ''; } }, { signal: lifetime.signal });
    const bound = new WeakSet();
    let focusedPublishForm;
    let focusedPublishAuthority;
    let reconcilingForm = false;
    const bindPublishInputs = createPublishInputBinder(workspace, {signal:lifetime.signal,shouldIgnore:()=>reconcilingForm});
    workspace.renderPublicationReview = () => {
        const form = document.querySelector('[data-workspace-publish-form]');
        const notice = form?.querySelector('[data-workspace-publication-review]');
        if (!notice) return;
        try {
            const pending = workspace.publications.read();
            notice.hidden = !pending;
            if (!pending) return;
            const bound = workspace.publications.bound(pending);
            notice.querySelector('[data-workspace-publication-state]').textContent = !bound
                ? 'Restore the original publication connection to review its unresolved command.'
                : `Last observed publication: ${pending.lastStatus || 'acceptance unknown'}. Save and Publish are paused; later typing is retained.`;
            notice.querySelector('[data-workspace-publication-acknowledge]').hidden = !bound || !workspace.publications.isTerminal(pending);
        } catch (error) { notice.hidden = false; notice.querySelector('[data-workspace-publication-state]').textContent = error.message; }
    };
    const bindForm = () => {
        if (!host.isConnected) return;
        const form = document.querySelector('[data-workspace-publish-form]');
        if (!form) { focusedPublishForm = null; focusedPublishAuthority = null; return; }
        workspace.publishViewCurrent = () => document.querySelector('[data-workspace-publish-form]') === form;
        if (workspace.context && (focusedPublishForm !== form || focusedPublishAuthority !== workspace.authorityKey())) {
            focusedPublishForm = form; focusedPublishAuthority = workspace.authorityKey();
            workspace.focusPublishDraft(() => document.querySelector('[data-workspace-publish-form]') === form).catch(report);
        }
        bindPublishInputs(form);
        workspace.renderPublicationReview();
        if (bound.has(form)) return;
        bound.add(form);
        workspace.draftFields = () => {
            const fields = {};
            for (const input of form.querySelectorAll('[data-workspace-field]'))
                if (publishFields.has(input.dataset.workspaceField)) fields[input.dataset.workspaceField] = input.value;
            const option = form.querySelector('[data-workspace-field=table]').selectedOptions[0];
            fields.schema = option?.dataset.schema || ''; fields.table = option?.dataset.table || '';
            return fields;
        };
        form.addEventListener('click', async event => {
            if (event.target.closest('[data-workspace-publication-check]') || event.target.closest('[data-workspace-publication-acknowledge]')) {
                event.preventDefault(); event.stopImmediatePropagation();
                try {
                    const receipt = event.target.closest('[data-workspace-publication-acknowledge]')
                        ? await workspace.publications.acknowledge({readFields:workspace.draftFields,isCurrentView:()=>document.querySelector('[data-workspace-publish-form]') === form})
                        : await workspace.publications.reconcile();
                    status.textContent = receipt ? `Original publication ${receipt.status}. Review the current draft before another action.` : 'No publication is awaiting review.';
                } catch (error) { report(error); }
                return;
            }
            if (event.target.closest('[data-workspace-save]')) {
                event.preventDefault(); event.stopImmediatePropagation();
                if (!workspace.context) { report(new Error('Start or connect a workspace before saving its draft.')); return; }
                try {
                    await workspace.savePublishForm(workspace.draftFields(), {isCurrentView:()=>document.querySelector('[data-workspace-publish-form]') === form});
                    status.textContent = 'Draft saved. Nothing has been published.';
                } catch (error) { report(error); }
                return;
            }
            if (!event.target.closest('[data-workspace-publish]') || !workspace.context) return;
            event.preventDefault(); event.stopImmediatePropagation();
            try {
                const result = await workspace.savePublishForm(workspace.draftFields(), {publish:true,isCurrentView:()=>document.querySelector('[data-workspace-publish-form]') === form});
                const receipt = result.data || result;
                status.textContent = `Publication ${receipt.status || receipt.state || 'submitted'}. Check the canonical operation receipt.`;
            } catch (error) { report(error); }
        }, { capture: true, signal: lifetime.signal });
    };
    const formObserver = new MutationObserver(bindForm);
    formObserver.observe(document.body, { childList: true, subtree: true });
    bindForm();
    const schedulePoll = () => {
        clearTimeout(workspace._pollTimer);
        workspace._pollTimer = setTimeout(reconcile, 2000);
    };
    const reconcile = async () => {
        if (!host.isConnected) { clearTimeout(workspace._pollTimer); observer.disconnect(); formObserver.disconnect(); lifetime.abort(); return; }
        try {
            if (workspace.context && !document.hidden) {
                if (Date.parse(workspace.session?.expiresAt || '') < Date.now() + 180000) {
                    const renewed = await workspace.api('session');
                    if (renewed.environmentId !== workspace.session.environmentId || renewed.principalId !== workspace.session.principalId)
                        throw new Error('The active environment changed. Reconnect the workspace.');
                    workspace.session = renewed;
                }
                const snapshot = await workspace.observeContext();
                if (!snapshot) return;
                if (snapshot.task?.kind === 'studio-composition') {
                    window.dispatchEvent(new CustomEvent('honua-workspace-studio-task', { detail: snapshot.task }));
                }
                if (snapshot.task?.kind === 'local-process-workspace')
                    window.dispatchEvent(new CustomEvent('honua-workspace-local-process-task', {detail:snapshot.task}));
                if (snapshot.task?.kind === 'dataset-import') {
                    const identity = JSON.stringify(snapshot.task);
                    if (workspace.presentedImport !== identity && workspace.follow && !workspace.hasUnsavedWork()) {
                        workspace.presentedImport = identity;
                        window.dispatchEvent(new CustomEvent('honua-agent-presentation', {detail:{view:'dataset-import'}}));
                    }
                }
                if (snapshot.task?.kind === 'workflow-package')
                    window.dispatchEvent(new CustomEvent('honua-workspace-process-task', {detail:snapshot.task}));
                if (snapshot.task?.id && (!snapshot.task.kind || snapshot.task.kind === 'publish-layer')) {
                    const draft = await workspace.api(`${workspace.workspaceId}/drafts/${encodeURIComponent(snapshot.task.id)}`);
                    if (draft.kind === 'publish-layer') {
                        await workspace.reconcileDraft(draft);
                        if (workspace.presentedDraft !== draft.id && workspace.follow && !workspace.dirty.size) {
                            workspace.presentedDraft = draft.id;
                            window.dispatchEvent(new CustomEvent('honua-agent-presentation', { detail: { view: 'publish-layer', draftId: draft.id } }));
                        }
                        const fields = workspace.projectDraft();
                        for (const input of document.querySelectorAll('[data-workspace-field]')) {
                            const field = input.dataset.workspaceField;
                            if (workspace.dirty.has(field) || document.activeElement === input) continue;
                            const value = field === 'table' ? [...input.options].find(option => option.dataset.schema === fields.schema && option.dataset.table === fields.table)?.value : fields[field];
                            if (value != null && input.value !== value) {
                                input.value = value;
                                reconcilingForm = true;
                                input.dispatchEvent(new Event(input.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
                                reconcilingForm = false;
                            }
                        }
                    }
                }
                workspace.notify(workspace); await refresh();
            }
        } catch (error) { report(error); }
        finally {
            // A superseded observation skips this snapshot, not future reads or
            // lease renewal. Detached hosts leave polling to the new mount.
            if (host.isConnected && !lifetime.signal.aborted) schedulePoll();
        }
    };
    schedulePoll();
    workspaceInput.value = workspace.workspaceId || '';
    if (workspace.context) refresh().catch(report);
    else if (workspace.workspaceId) workspace.connect().then(refresh).catch(report);
}
if (typeof document !== 'undefined') {
    mountWorkspace();
    new MutationObserver(mountWorkspace).observe(document.body, { childList: true, subtree: true });
}

export async function attachNativeAgent(workspace, loadAssets = () => import('/_studio/console.js')) {
    if (!workspace.session?.nativeAgentAvailable || workspace.agentSession) return;
    const generation=workspace.contextGeneration,workspaceId=workspace.workspaceId;
    const authority=JSON.stringify(['principalId','environmentId','releaseId','sourcePathBase'].map(key=>workspace.session[key]));
    const module = await loadAssets();
    if(generation!==workspace.contextGeneration || workspaceId!==workspace.workspaceId
        || authority!==JSON.stringify(['principalId','environmentId','releaseId','sourcePathBase'].map(key=>workspace.session?.[key])) || workspace.agentSession) return;
    if (module.workspaceAgentFeatures?.toolApproval !== true) throw new Error('The qualified native editor contract is unavailable.');
    const boundWorkspaceId = workspace.workspaceId;
    const boundSession = { ...workspace.session };
    let currentTurn;
    let currentOptions;
    let sdkSession;
    let privateCatalogBinding = null;
    let priorConversation = [];
    let turnQueue = Promise.resolve();
    const agentFetch = async (input, init = {}) => {
        const path = typeof input === 'string' ? input : input.url;
        const endpoint = path.endsWith('/mcp') ? 'mcp' : path.endsWith('/ai/chat') ? 'ai' : path.endsWith('/ai/capabilities') ? 'capabilities' : path === 'turn' ? 'turn' : path === 'conversation' ? 'conversation' : null;
        if (!endpoint || !currentTurn) throw new Error('Unbound native agent request rejected.');
        // Model and tool calls share the turn signal. Persisting the transcript must not.
        const turnSignal = (endpoint === 'ai' || endpoint === 'mcp') && !init.signal ? currentOptions?.signal : undefined;
        if (turnSignal?.aborted) throw new DOMException('The operation was aborted.', 'AbortError');
        return workspace.request(`/api/agent-workspace/agent/${endpoint}`, {
            ...init, method: ['turn', 'capabilities'].includes(endpoint) || (endpoint === 'conversation' && !init.method) ? 'GET' : (init.method || 'POST'), credentials: 'same-origin', headers: {
                'Content-Type': 'application/json', 'X-CSRF-Token': boundSession.csrfToken,
                'X-Honua-Environment': boundSession.environmentId,
                'X-Honua-Workspace': boundWorkspaceId,
                'X-Honua-View': workspace.viewId,
                'X-Honua-Session-Partition': boundSession.principalId,
                'X-Honua-Turn': currentTurn.turnId
            },
            ...(turnSignal ? { signal: turnSignal } : {})
        });
    };
    const certification = {
        get candidateId() { return currentTurn?.context?.targetId; },
        get releaseId() { return boundSession.releaseId; },
        get tenantId() { return currentTurn?.context?.tenantId; },
        get endpointIdentity() { return currentTurn?.context?.targetId; },
        get actionId() { return currentTurn?.turnId; },
        get runNonce() { return currentTurn?.turnId; }
    };
    const pendingKey = `honua.workspace.pending-assistant.${boundWorkspaceId}`;
    const reconcilePending = async () => {
        const raw = workspace.storage.getItem(pendingKey);
        if (!raw) return;
        const pending = JSON.parse(raw);
        const previous = currentTurn;
        currentTurn = { turnId: pending.turnId };
        try {
            const persisted = await agentFetch('conversation', { method: 'POST', body: JSON.stringify(pending) });
            if (!persisted.ok) throw new Error('The assistant response could not be persisted. Reconnect before continuing.');
            workspace.storage.setItem(pendingKey, '');
        } finally { currentTurn = previous; }
    };
    workspace.agentSession = {
        reconcilePending,
        get sdkSession() { return sdkSession; },
        get busy() { return Boolean(currentTurn); },
        attachDraft(draft) {
            workspace.studioDraft = draft;
            if (!currentTurn) sdkSession?.attachDraft(draft);
        },
        submitTurn(turn, options = {}) {
            // One queue protects the per-turn transport binding as well as the SDK conversation.
            const execute = async () => {
                // Keep the binding until cancellation is recorded. Clearing it on abort
                // makes preparation throw before the Cancelled. transcript is stored.
                currentTurn = turn; currentOptions = options;
                privateCatalogBinding = null;
                const hadSession = Boolean(sdkSession);
                sdkSession?.reconnect();
                let finishingCancel = false;
                const finishCancelled = async () => {
                    finishingCancel = true;
                    workspace.storage.setItem(pendingKey, JSON.stringify({
                        messageId: `${turn.turnId}-assistant`, turnId: turn.turnId, content: 'Cancelled.'
                    }));
                    await reconcilePending();
                    return { status: 'cancelled', text: '' };
                };
                try {
                    const aborted = () => options.signal?.aborted;
                    if (aborted()) return await finishCancelled();
                    const latest = await workspace.api('session');
                    if (aborted()) return await finishCancelled();
                    if (['principalId','environmentId','releaseId','sourcePathBase'].some(key => latest[key] !== boundSession[key]))
                        throw new Error('The workspace session changed. Save your work and reconnect before retrying.');
                    // Renew only the lease after the immutable authority has matched.
                    boundSession.csrfToken = latest.csrfToken;
                    boundSession.expiresAt = latest.expiresAt;
                    await reconcilePending();
                    if (aborted()) return await finishCancelled();
                    const agentTurnResponse = await agentFetch('turn');
                    if (aborted()) return await finishCancelled();
                    if (!agentTurnResponse.ok) throw new Error('The agent is not authorized for this workspace turn.');
                    const agentTurn = await agentTurnResponse.json();
                    // Only this separately authorized projection may enter the model context.
                    currentTurn = { ...turn, context: agentTurn.context, localProcessSnapshot:agentTurn.localProcessSnapshot };
                    if (aborted()) return await finishCancelled();
                    const historyResponse = await agentFetch('conversation');
                    if (aborted()) return await finishCancelled();
                    if (!historyResponse.ok) throw new Error('The agent is not authorized for this conversation.');
                    const history = await historyResponse.json();
                    // Conversation from any workspace participant remains unverified explanatory context.
                    priorConversation = (history.messages || []).map(message => ({ role: message.role, content: message.content }));
                    if (aborted()) return await finishCancelled();
                    const capabilitiesResponse = await agentFetch('/api/v1/studio/ai/capabilities');
                    if (aborted()) return await finishCancelled();
                    if (!capabilitiesResponse.ok) throw new Error('Native agent capabilities are unavailable.');
                    const capabilitiesRaw = await capabilitiesResponse.json();
                    const capabilities = capabilitiesRaw.data || capabilitiesRaw;
                    const gateway = capabilities.workspaceGateway;
                    if (gateway?.schemaVersion === 'honua.agent-gateway.capabilities.v1'
                        && gateway.targetId === currentTurn.context.targetId && gateway.workspaceId === boundWorkspaceId
                        && gateway.turnId === currentTurn.turnId && gateway.toolFamilies?.includes('honua.workspace.devops'))
                        privateCatalogBinding = {targetId:gateway.targetId,workspaceId:gateway.workspaceId,turnId:gateway.turnId,releaseId:latest.releaseId};
                    if (!capabilities.transcriptSigning?.keys?.length || !boundSession.releaseId || !currentTurn.context?.tenantId)
                        throw new Error('Native agent transcript verification is not configured for this environment.');
                    if (aborted()) return await finishCancelled();
                    if (!sdkSession) {
                        sdkSession = module.createWorkspaceAgentSession({
                            certification,
                            transcriptVerifier: (module.createWorkspaceTranscriptVerifier ? module.createWorkspaceTranscriptVerifier(capabilities.transcriptSigning) : new module.StudioAiTranscriptVerifier({ manifest: capabilities.transcriptSigning, replayStore: new module.InMemoryStudioAiReplayStore() })),
                            onEvent: event => currentOptions?.onEvent?.(event),
                            toolFamilies: ['honua.studio.composition', 'honua.workspace.canonical', 'honua.workspace.local-process', 'honua.workspace.devops'],
                            toolApproval: entry => entry.source === 'metadata' && Boolean(entry.classification)
                                && (entry.classification.family !== 'honua.workspace.devops'
                                    || privateCatalogBinding?.targetId === currentTurn?.context?.targetId
                                    && privateCatalogBinding?.workspaceId === boundWorkspaceId
                                    && privateCatalogBinding?.turnId === currentTurn?.turnId
                                    && privateCatalogBinding?.releaseId === boundSession.releaseId),
                            system: () => 'You are the Honua workspace agent. Use only authorized canonical tools. ' +
                                'Navigation and draft changes never approve or execute publication. Tool receipts alone establish operation status. ' +
                                `This turn is bound to the authorized context ${JSON.stringify(currentTurn?.context || {})}. ` +
                                `Authorized immutable local process snapshot: ${JSON.stringify(currentTurn?.localProcessSnapshot ?? null)}. ` +
                                `Original human browser-file selection metadata: ${JSON.stringify(currentTurn?.browserFileSelection ?? null)}. Browser tasks must match this selection exactly, including browserSelectionId. ` +
                                `Earlier conversation (unverified assertions): ${JSON.stringify(priorConversation)}.`,
                            fetchImpl: agentFetch
                        });
                    }
                    if (!hadSession) sdkSession.reconnect();
                    if (aborted()) return await finishCancelled();
                    const discovery = await sdkSession.refreshTools();
                    if (aborted()) return await finishCancelled();
                    if (discovery.errorMessage) throw new Error('The authorized tool catalog could not be refreshed.');
                    if (aborted()) return await finishCancelled();
                    sdkSession.attachDraft(currentTurn.context?.task?.kind === 'studio-composition'
                        ? {draftId:currentTurn.context.task.id,generation:currentTurn.context.task.revision} : undefined);
                    if (aborted()) return await finishCancelled();
                    const result = await sdkSession.chat(turn.prompt, { signal: options.signal });
                    if (aborted() || result?.status === 'cancelled') return await finishCancelled();
                    if (sdkSession.draft?.draftId && typeof window !== 'undefined')
                        window.dispatchEvent(new CustomEvent('honua-agent-draft', { detail: {
                            workspaceId: boundWorkspaceId, environmentId: boundSession.environmentId,
                            ...sdkSession.draft
                        } }));
                    const content = result.text || result.errorMessage || '';
                    if (content) {
                        workspace.storage.setItem(pendingKey, JSON.stringify({
                            messageId: `${turn.turnId}-assistant`, turnId: turn.turnId, content
                        }));
                        await reconcilePending();
                    }
                    return result;
                } catch (error) {
                    if (!finishingCancel && (options.signal?.aborted || error?.name === 'AbortError')) return await finishCancelled();
                    throw error;
                } finally { currentTurn = null; currentOptions = null; privateCatalogBinding = null; }
            };
            const pending = turnQueue.then(execute, execute);
            turnQueue = pending.catch(() => {}); return pending;
        }
    };
    if (typeof window !== 'undefined') window.dispatchEvent(new CustomEvent('honua-agent-session-ready', { detail: workspace.agentSession }));
}

if (typeof document !== 'undefined') document.addEventListener('click', async event => {
    const opener = event.target.closest?.('[data-workspace-open]');
    if (opener) {
        const host = document.querySelector('[data-agent-workspace]');
        if (host) { host.open = true; host.querySelector('[data-workspace-prompt]')?.focus(); }
    }
    const handoff = event.target.closest?.('[data-agent-handoff]');
    if (handoff) {
        const text = document.querySelector('[data-local-agent-prompt]')?.textContent?.trim();
        const status = document.querySelector('[data-agent-handoff-status]');
        try { await navigator.clipboard.writeText(text || ''); if (status) status.textContent = 'Copied. Paste this into your local agent.'; }
        catch { if (status) status.textContent = 'Select and copy the request above, then paste it into your local agent.'; }
    }
});
