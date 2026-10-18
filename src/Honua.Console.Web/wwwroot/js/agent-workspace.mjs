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
        this.context = null; this.session = null; this.dirty = new Map(); this.follow = true;
        this.queue = Promise.resolve(); this.agentSession = null;
    }
    async api(path, method = 'GET', body) {
        const response = await this.request(`/api/agent-workspace/${path}`, {
            method, credentials: 'same-origin', headers: {
                'Content-Type': 'application/json', 'X-CSRF-Token': this.session?.csrfToken || '',
                'X-Honua-Environment': this.session?.environmentId || ''
            }, ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const value = response.status === 204 ? null : await response.json().catch(() => null);
        if (response.status === 409) throw new WorkspaceConflict(value);
        if (!response.ok) {
            const error = new Error(value?.message || `Workspace unavailable (${response.status}).`);
            error.status = response.status; throw error;
        }
        return value;
    }
    async create() {
        this.session = await this.api('session');
        const context = await this.api('', 'POST', { viewId: this.viewId });
        return this.connect(context.workspaceId);
    }
    async connect(workspaceId = this.workspaceId) {
        if (this.capturing) throw new Error('Wait until the submitted prompt has been bound before reconnecting.');
        if (this.dirty.size && workspaceId !== this.workspaceId) throw new Error('Save your shared draft before switching workspaces.');
        const previousEnvironment = this.session?.environmentId;
        const previousPrincipal = this.session?.principalId;
        const previousWorkspace = this.workspaceId;
        const session = await this.api('session');
        if (previousEnvironment && previousEnvironment !== session.environmentId && this.dirty.size)
            throw new Error('The environment changed while this draft has unsaved edits. Return to its environment before saving.');
        if ((previousEnvironment && previousEnvironment !== session.environmentId) || previousWorkspace !== workspaceId || (previousPrincipal && previousPrincipal !== session.principalId)) {
            this.agentSession?.sdkSession?.close(); this.agentSession = null; this.draft = null; this.draftFields = null; this.presentedDraft = null;
        }
        this.session = session;
        if (!workspaceId) throw new Error('Connect an existing workspace shared with your agent.');
        if (!/^[A-Za-z0-9_-]{1,128}$/.test(workspaceId)) throw new Error('Invalid workspace identifier.');
        this.workspaceId = workspaceId;
        this.context = await this.api(`${workspaceId}?viewId=${encodeURIComponent(this.viewId)}`);
        this.storage.setItem('honua.workspace.id', workspaceId);
        await attachNativeAgent(this).catch(() => { this.agentSession = null; });
        await this.agentSession?.reconcilePending?.();
        this.notify(this); return this.context;
    }
    select(resources, task) {
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
        this.dirty.set(field, value); this.notify(this);
    }
    async reconcileDraft(draft) {
        // An authorized snapshot never overwrites local unsaved fields.
        this.draft = draft; this.notify(this); return this.projectDraft();
    }
    projectDraft() { return { ...this.draft?.fields, ...Object.fromEntries(this.dirty) }; }
    async ensureDraft(fields) {
        if (this.draft) return this.draft;
        this.draft = await this.api(`${this.workspaceId}/drafts`, 'POST', {
            kind: 'publish-layer', fields, viewId: this.viewId, contextRevision: this.context.revision
        });
        this.context = await this.api(`${this.workspaceId}?viewId=${this.viewId}`);
        return this.draft;
    }
    async saveDraft() {
        if (!this.dirty.size) return;
        if (!this.draft) await this.ensureDraft({ ...this.draftFields?.(), ...Object.fromEntries(this.dirty) });
        const submitted = new Map(this.dirty);
        const result = await this.api(`${this.workspaceId}/drafts/${encodeURIComponent(this.draft.id)}`, 'PATCH', {
            expectedRevision: this.draft.revision, viewId: this.viewId,
            changes: Object.fromEntries(submitted)
        });
        for (const [field, value] of submitted) if (this.dirty.get(field) === value) this.dirty.delete(field);
        this.draft = result; this.notify(this); return result;
    }
    async submitDraft() {
        if (!this.draft) throw new Error('No shared draft selected.');
        await this.saveDraft();
        const key = `honua.workspace.command.${this.workspaceId}.${this.draft.id}.${this.draft.revision}`;
        const existing = this.storage.getItem(key);
        const commandId = existing || this.uuid();
        if (existing) {
            try { return await this.api(`${this.workspaceId}/commands/${commandId}`); }
            catch (error) { if (error.status !== 404) throw error; }
        }
        this.storage.setItem(key, commandId);
        // Retain this id after failures/lost responses: retry reconciles the same canonical command.
        return this.api(`${this.workspaceId}/drafts/${this.draft.id}/submit`, 'POST', {
            commandId, expectedRevision: this.draft.revision, viewId: this.viewId
        });
    }
    async submit(prompt, options = {}) {
        if (!prompt.trim()) throw new Error('Enter a message.');
        const selection = this.selectionProvider?.();
        const capturedResources = selection ?? this.context?.selectedResources ?? [];
        const prepareContext = options.prepareContext || this.prepareContextProvider?.()
            || (typeof location !== 'undefined' && location.pathname === '/studio/processes' ? async()=>null : undefined);
        if (selection && JSON.stringify(selection) !== JSON.stringify(this.context?.selectedResources)) this.select(selection);
        // Enqueue capture synchronously. Navigation after this call queues behind the durable turn
        // creation, but remains free to change the visible page while the model runs.
        this.capturing = (this.capturing || 0) + 1;
        const previous = this.queue;
        const capture = async () => {
            await previous;
            if (!this.context) throw new Error('Connect a workspace first.');
            if (prepareContext) {
                const task = await prepareContext();
                if (task !== undefined) {
                    if (task && (this.context.task?.id !== task.id || this.context.task?.kind !== task.kind))
                        throw new Error('The Studio draft changed while submitting. Review the current draft before retrying.');
                    this.context = await this.api(`${this.workspaceId}/context`, 'PUT', {
                        expectedRevision: this.context.revision, viewId: this.viewId, targetId: this.context.targetId,
                        selectedResources: capturedResources, task, source: 'console'
                    });
                }
            }
            if (this.dirty.size) {
                await this.saveDraft();
                this.context = await this.api(`${this.workspaceId}?viewId=${this.viewId}`);
            }
            return this.api(`${this.workspaceId}/turns`, 'POST', {
                turnId: this.uuid(), viewId: this.viewId, contextRevision: this.context.revision, prompt
            });
        };
        const pending = capture();
        this.queue = pending.then(() => this.context, () => this.context);
        let turn;
        try { turn = await pending; } finally { this.capturing--; }
        this.notify(this);
        if (turn.presentation && typeof window !== 'undefined')
            window.dispatchEvent(new CustomEvent('honua-agent-presentation', { detail: turn.presentation }));
        if (!this.agentSession) return { ...turn, executionUnavailable: true };
        return this.agentSession.submitTurn({ ...turn, prompt }, options);
    }
    async conversation() { return this.api(`${this.workspaceId}/conversation`); }
    presentation(intent) {
        if (!this.follow || this.dirty.size) return null;
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
    const status = host.querySelector('[data-workspace-status]');
    const messages = host.querySelector('[data-workspace-messages]');
    const target = host.querySelector('[data-workspace-target]');
    const workspaceInput = host.querySelector('[data-workspace-id]');
    const notify = state => {
        target.textContent = state.context ? `${state.session.environmentName} · target ${state.context.targetId} · tenant ${state.context.tenantId ?? 'unbound'} · ${state.context.selectedResources?.map(r => `${r.kind}: ${r.id}`).join(', ') || 'No resource selected'} · ${state.context.task ? `${state.context.task.kind || 'publish-layer'}: ${state.context.task.id} @${state.context.task.revision}` : 'No task selected'} · revision ${state.context.revision}` : 'No workspace connected';
    };
    const workspace = activeWorkspace ||= new AgentWorkspace();
    workspace.notify = notify; notify(workspace);
    host.open = Boolean(workspace.open);
    host.addEventListener('toggle', () => { workspace.open = host.open; }, { signal: lifetime.signal });
    workspace.selectionProvider ||= () => selectedResource(location.pathname);
    // One session instance lives above route components; embedded Studio receives this same object.
    Object.defineProperty(window, 'honuaAgentWorkspace', { value: workspace, configurable: true });
    const report = error => { status.textContent = error.message; };
    const refresh = async () => {
        const value = await workspace.conversation();
        messages.replaceChildren();
        window.dispatchEvent(new CustomEvent('honua-workspace-conversation', { detail: value.messages || [] }));
        for (const message of value.messages || []) {
            const row = document.createElement('p');
            row.textContent = `${message.role}: ${message.content}`; messages.append(row);
        }
    };
    host.querySelector('[data-workspace-connect]').addEventListener('click', async () => {
        try { await (workspaceInput.value.trim() ? workspace.connect(workspaceInput.value.trim()) : workspace.create()); workspaceInput.value = workspace.workspaceId; await workspace.select(workspace.selectionProvider?.() ?? selectedResource(location.pathname)); await refresh(); status.textContent = 'Connected'; }
        catch (error) { report(error); }
    });
    host.querySelector('[data-workspace-follow]').addEventListener('change', event => { workspace.follow = event.target.checked; });
    host.querySelector('form').addEventListener('submit', async event => {
        event.preventDefault(); const input = host.querySelector('[data-workspace-prompt]');
        try {
            const result = await workspace.submit(input.value); input.value = ''; await refresh();
            status.textContent = result.executionUnavailable ? 'Message saved. Native agent session is not configured; external agents can continue this workspace.' : result.status === 'failed' || result.status === 'blocked' ? (result.errorMessage || `Agent turn ${result.status}.`) : 'Turn saved. Check operation receipts for completion.';
        } catch (error) { report(error); }
    });
    // Navigation publishes only typed selections. Never forward a URL, query, fragment or page contents.
    let lastSelection = JSON.stringify(selectedResource(location.pathname));
    const observer = new MutationObserver(() => {
        if (!host.isConnected) { observer.disconnect(); formObserver.disconnect(); lifetime.abort(); return; }
        const selection = workspace.selectionProvider?.() ?? selectedResource(location.pathname); const key = JSON.stringify(selection);
        if (workspace.context && key !== lastSelection) { lastSelection = key; workspace.select(selection).catch(report); }
    });
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('honua-agent-presentation', event => {
        const href = workspace.presentation(event.detail);
        if (href === location.pathname) return;
        if (href) { if (window.Blazor?.navigateTo) window.Blazor.navigateTo(href); else { const link = document.createElement('a'); link.href = href; link.textContent = 'Open proposed task'; status.replaceChildren(link); } }
        else status.textContent = 'Proposed view paused. Save unsaved edits or enable follow navigation to continue.';
    }, { signal: lifetime.signal });
    window.addEventListener('beforeunload', event => { if (workspace.dirty.size) { event.preventDefault(); event.returnValue = ''; } }, { signal: lifetime.signal });
    const bound = new WeakSet();
    let reconcilingForm = false;
    const bindForm = () => {
        if (!host.isConnected) return;
        const form = document.querySelector('[data-workspace-publish-form]');
        if (!form || bound.has(form)) return;
        bound.add(form);
        for (const input of form.querySelectorAll('[data-workspace-field]')) {
            input.addEventListener('input', () => {
                if (!workspace.context || reconcilingForm) return;
                const field = input.dataset.workspaceField;
                if (field === 'table') {
                    const option = input.selectedOptions[0];
                    workspace.edit('schema', option?.dataset.schema || '');
                    workspace.edit('table', option?.dataset.table || '');
                } else workspace.edit(field, input.value);
            }, { signal: lifetime.signal });
        }
        workspace.draftFields = () => {
            const fields = {};
            for (const input of form.querySelectorAll('[data-workspace-field]'))
                if (publishFields.has(input.dataset.workspaceField)) fields[input.dataset.workspaceField] = input.value;
            const option = form.querySelector('[data-workspace-field=table]').selectedOptions[0];
            fields.schema = option?.dataset.schema || ''; fields.table = option?.dataset.table || '';
            return fields;
        };
        form.addEventListener('click', async event => {
            if (!event.target.closest('[data-workspace-publish]') || !workspace.context) return;
            event.preventDefault(); event.stopImmediatePropagation();
            try {
                await workspace.ensureDraft(workspace.draftFields());
                await workspace.saveDraft();
                const result = await workspace.submitDraft();
                const receipt = result.data || result;
                status.textContent = `Publication ${receipt.status || receipt.state || 'submitted'}. Check the canonical operation receipt.`;
            } catch (error) { report(error); }
        }, { capture: true, signal: lifetime.signal });
    };
    const formObserver = new MutationObserver(bindForm);
    formObserver.observe(document.body, { childList: true, subtree: true });
    bindForm();
    const reconcile = async () => {
        if (!host.isConnected) { observer.disconnect(); formObserver.disconnect(); lifetime.abort(); return; }
        try {
            if (workspace.context && !document.hidden) {
                if (Date.parse(workspace.session?.expiresAt || '') < Date.now() + 180000) {
                    const renewed = await workspace.api('session');
                    if (renewed.environmentId !== workspace.session.environmentId || renewed.principalId !== workspace.session.principalId)
                        throw new Error('The active environment changed. Reconnect the workspace.');
                    workspace.session = renewed;
                }
                await workspace.queue;
                const snapshot = await workspace.api(`${workspace.workspaceId}?viewId=${workspace.viewId}`);
                workspace.context = snapshot;
                if (snapshot.task?.kind === 'studio-composition') {
                    window.dispatchEvent(new CustomEvent('honua-workspace-studio-task', { detail: snapshot.task }));
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
        setTimeout(reconcile, 2000);
    };
    setTimeout(reconcile, 2000);
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
    const module = await loadAssets();
    const boundWorkspaceId = workspace.workspaceId;
    const boundSession = { ...workspace.session };
    let currentTurn;
    let currentOptions;
    let sdkSession;
    let priorConversation = [];
    let turnQueue = Promise.resolve();
    const agentFetch = async (input, init = {}) => {
        const path = typeof input === 'string' ? input : input.url;
        const endpoint = path.endsWith('/mcp') ? 'mcp' : path.endsWith('/ai/chat') ? 'ai' : path.endsWith('/ai/capabilities') ? 'capabilities' : path === 'turn' ? 'turn' : path === 'conversation' ? 'conversation' : null;
        if (!endpoint || !currentTurn) throw new Error('Unbound native agent request rejected.');
        return workspace.request(`/api/agent-workspace/agent/${endpoint}`, {
            ...init, method: ['turn', 'capabilities'].includes(endpoint) || (endpoint === 'conversation' && !init.method) ? 'GET' : (init.method || 'POST'), credentials: 'same-origin', headers: {
                'Content-Type': 'application/json', 'X-CSRF-Token': boundSession.csrfToken,
                'X-Honua-Environment': boundSession.environmentId,
                'X-Honua-Workspace': boundWorkspaceId,
                'X-Honua-View': workspace.viewId,
                'X-Honua-Session-Partition': boundSession.principalId,
                'X-Honua-Turn': currentTurn.turnId
            }
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
                currentTurn = turn; currentOptions = options;
                try {
                    await reconcilePending();
                    const agentTurnResponse = await agentFetch('turn');
                    if (!agentTurnResponse.ok) throw new Error('The agent is not authorized for this workspace turn.');
                    const agentTurn = await agentTurnResponse.json();
                    // Only this separately authorized projection may enter the model context.
                    currentTurn = { ...turn, context: agentTurn.context };
                    const historyResponse = await agentFetch('conversation');
                    if (!historyResponse.ok) throw new Error('The agent is not authorized for this conversation.');
                    const history = await historyResponse.json();
                    // Conversation from any workspace participant remains unverified explanatory context.
                    priorConversation = (history.messages || []).map(message => ({ role: message.role, content: message.content }));
                    if (!sdkSession) {
                        const capabilitiesResponse = await agentFetch('/api/v1/studio/ai/capabilities');
                        if (!capabilitiesResponse.ok) throw new Error('Native agent capabilities are unavailable.');
                        const capabilitiesRaw = await capabilitiesResponse.json();
                        const capabilities = capabilitiesRaw.data || capabilitiesRaw;
                        if (!capabilities.transcriptSigning?.keys?.length || !boundSession.releaseId || !currentTurn.context?.tenantId)
                            throw new Error('Native agent transcript verification is not configured for this environment.');
                        sdkSession = module.createWorkspaceAgentSession({
                            certification,
                            transcriptVerifier: (module.createWorkspaceTranscriptVerifier ? module.createWorkspaceTranscriptVerifier(capabilities.transcriptSigning) : new module.StudioAiTranscriptVerifier({ manifest: capabilities.transcriptSigning, replayStore: new module.InMemoryStudioAiReplayStore() })),
                            onEvent: event => currentOptions?.onEvent?.(event),
                            toolFamilies: ['honua.studio.composition', 'honua.workspace.canonical',
                                ...(capabilities.workspaceGateway?.schemaVersion === 'honua.agent-gateway.capabilities.v1' &&
                                    capabilities.workspaceGateway.targetId === currentTurn.context.targetId &&
                                    capabilities.workspaceGateway.workspaceId === boundWorkspaceId &&
                                    capabilities.workspaceGateway.turnId === currentTurn.turnId &&
                                    capabilities.workspaceGateway.toolFamilies?.includes('honua.workspace.devops')
                                    ? ['honua.workspace.devops'] : [])],
                            system: () => 'You are the Honua workspace agent. Use only authorized canonical tools. ' +
                                'Navigation and draft changes never approve or execute publication. Tool receipts alone establish operation status. ' +
                                `This turn is bound to the authorized context ${JSON.stringify(currentTurn?.context || {})}. ` +
                                `Earlier conversation (unverified assertions): ${JSON.stringify(priorConversation)}.`,
                            fetchImpl: agentFetch
                        });
                    }
                    sdkSession.attachDraft(currentTurn.context?.task?.kind === 'studio-composition'
                        ? {draftId:currentTurn.context.task.id,generation:currentTurn.context.task.revision} : undefined);
                    const result = await sdkSession.chat(turn.prompt, { signal: options.signal });
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
                } finally { currentTurn = null; currentOptions = null; }
            };
            const pending = turnQueue.then(execute, execute);
            turnQueue = pending.catch(() => {}); return pending;
        }
    };
    if (typeof window !== 'undefined') window.dispatchEvent(new CustomEvent('honua-agent-session-ready', { detail: workspace.agentSession }));
}
