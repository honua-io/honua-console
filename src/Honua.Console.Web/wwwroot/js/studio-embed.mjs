import { selectedResource, studioResourceRefs } from './agent-workspace.mjs';
let assets;
let element;
const elementsByEnvironment = new Map();
let session;
let processView;
function studioElement(session) {
    let app = elementsByEnvironment.get(session.environmentId);
    if (!app) {
        app = document.createElement('honua-studio-app');
        app.routingMode = 'host'; app.setAttribute('base-path', '/studio');
        app.addEventListener('honua-studio-navigate', event => {
            if (/^\/studio(?:\/[A-Za-z0-9_-]+)*$/.test(event.detail?.path || ''))
                window.Blazor?.navigateTo(event.detail.path);
        });
        app.addEventListener('honua-workspace-draft-change', event => {
            app.workspaceDraftIdentity = event.detail;
            app.workspaceTaskKey = event.detail ? `${event.detail.draftId}:${event.detail.generation}` : undefined;
            const workspace = window.honuaAgentWorkspace;
            if (workspace?.session?.environmentId === session.environmentId) {
                workspace.agentSession?.attachDraft(event.detail ? {draftId:event.detail.draftId,generation:event.detail.generation} : undefined);
                if (!event.detail) { workspace.select(undefined,null).catch(()=>{}); return; }
                if (workspace.context && event.detail.packageKey)
                    workspace.select(undefined, {
                        id:event.detail.draftId,revision:event.detail.generation,
                        kind:'studio-composition',packageKey:event.detail.packageKey
                    }).catch(error => {
                        const status=document.querySelector('[data-studio-status]');
                        if(status) status.textContent=error.message;
                    });
            }
        });
        app.addEventListener('honua-studio-lifecycle-activity',async event=>{
            if(!['draft-loaded','version-reopened'].includes(event.detail?.kind) || !event.detail?.draftId) return;
            try { await app.prepareWorkspaceTurn(); await app.attachWorkspaceDraft(event.detail.draftId); }
            catch(error) { const status=document.querySelector('[data-studio-status]');if(status) status.textContent=error.message; }
        });
        app.addEventListener('honua-studio-selection-change', event => {
            const workspace = window.honuaAgentWorkspace;
            if (app.isConnected && workspace?.context && workspace.session?.environmentId === session.environmentId) workspace.select(studioResourceRefs(event.detail?.targets,
                app.composition.state.layers, app.sourceCatalog)).catch(error => {
                    const status = document.querySelector('[data-studio-status]');
                    if (status) status.textContent = error.message;
                });
        });
        elementsByEnvironment.set(session.environmentId, app);
    }
    app.session = { kind: 'cookie', baseUrl: '/api/studio', expiresAt: session.expiresAt,
        csrfToken: session.csrfToken, environmentId: session.environmentId, principalId: session.principalId };
    app.workspace = window.honuaAgentWorkspace;
    if (app.workspace) app.workspace.selectionProvider = () => app.isConnected
        ? studioResourceRefs(app.composition.selection, app.composition.state.layers, app.sourceCatalog)
        : selectedResource(location.pathname);
    if (app.workspace) app.workspace.prepareContextProvider = () => {
        if (!app.isConnected) return location.pathname === '/studio/processes' ? processContextPreparation() : undefined;
        const expectedDraftId = app.workspaceDraftIdentity?.draftId;
        return async () => {
            const draft = await app.prepareWorkspaceTurn();
            if (draft?.draftId !== expectedDraftId)
                throw new Error('The Studio draft changed while submitting. Review it before retrying.');
            return draft ? {id:draft.draftId,revision:draft.generation,kind:'studio-composition',packageKey:draft.packageKey} : undefined;
        };
    };
    app.agentSessionAdapter = { submitTurn: (prompt, options) => window.honuaAgentWorkspace.submit(prompt, options) };
    return app;
}
async function mount() {
    const host = document.querySelector('[data-studio-embed]');
    if (!host || host.dataset.mounted) return;
    host.dataset.mounted = 'true';
    const status = host.querySelector('[data-studio-status]');
    try {
        assets ||= import('/_studio/console.js');
        const module = await assets;
        module.registerAllStudioElements();
        const response = await fetch('/api/agent-workspace/session', { credentials: 'same-origin' });
        if (!response.ok) throw new Error('Sign in to Honua to open Studio.');
        session = await response.json();
        element = studioElement(session);
        element.currentPath = location.pathname;
        host.querySelector('[data-studio-mount]').append(element);
        const createApp=element;
        const create=document.createElement('form');
        const key=document.createElement('input');key.required=true;key.maxLength=200;key.pattern='[A-Za-z0-9._-]{1,200}';
        key.placeholder='Map package key';key.setAttribute('aria-label','New map package key');
        const button=document.createElement('button');button.type='submit';button.textContent='New map';
        create.append(key,button);host.querySelector('[data-studio-mount]').before(create);
        create.addEventListener('submit',async event=>{
            event.preventDefault();if(!create.reportValidity()) return;button.disabled=true;
            try { await createApp.createWorkspaceDraft(key.value.trim());status.textContent='Shared server draft opened.'; }
            catch(error) {status.textContent=error.message;} finally {button.disabled=false;}
        });
        const task = element.workspace?.context?.task;
        const draft = task?.kind === 'studio-composition' ? {draftId:task.id}
            : !task ? element.workspace?.agentSession?.sdkSession?.draft : undefined;
        if (draft?.draftId) await element.attachWorkspaceDraft(draft.draftId);
        status.textContent = `Connected to ${session.environmentName}`;
        if (!document.querySelector('link[data-studio-style]')) {
            const link = document.createElement('link'); link.rel = 'stylesheet'; link.href = '/_studio/style.css';
            link.dataset.studioStyle = 'true'; document.head.append(link);
        }
    } catch (error) {
        status.textContent = 'Studio is unavailable. Verify the pinned Studio assets and your Honua session.';
        host.dataset.mounted = 'failed';
    }
}
new MutationObserver(mount).observe(document.body, { childList: true, subtree: true });
mount();

async function mountProcesses() {
    const host = document.querySelector('[data-processes-embed]');
    if (!host || host.dataset.mounted) return;
    host.dataset.mounted = 'true';
    const status = host.querySelector('[data-processes-status]');
    try {
        assets ||= import('/_studio/console.js');
        const module = await assets; module.registerAllStudioElements();
        const response = await fetch('/api/agent-workspace/session', { credentials: 'same-origin' });
        if (!response.ok) throw new Error('Sign in to Honua to open Processes.');
        const session = await response.json();
        const options = { environmentId: session.environmentId, csrfToken: session.csrfToken };
        const builtin = document.createElement('honua-studio-processes');
        builtin.client = new module.ServerProcessWorkspace(options);
        const custom = document.createElement('honua-studio-custom-processes');
        custom.client = new module.LocalProcessWorkspace(options);
        processView = {builtin,custom,host,mode:'builtin'};
        const workspace=window.honuaAgentWorkspace;
        if(workspace) {
            workspace.prepareContextProvider=()=> location.pathname === '/studio/processes' ? processContextPreparation() : undefined;
            if(workspace.context?.task?.kind === 'workflow-package') {
                const task=workspace.context.task;
                if(await builtin.attachWorkspaceTask({packageId:task.id,version:task.revision,contentHash:task.contentHash}))
                    await workspace.select([],task);
            } else await workspace.select([],null);
        }
        custom.addEventListener('focusin',()=>{processView.mode='custom';workspace?.select([],null).catch(()=>{});});
        builtin.addEventListener('focusin',()=>{processView.mode='builtin';});
        builtin.addEventListener('input',()=>workspace?.select(undefined,null).catch(()=>{}));
        const prepare=builtin.client.prepare.bind(builtin.client);
        builtin.client.prepare=async(...args)=>{
            const actual=await prepare(...args);
            const task=processTask(actual);
            if(host.isConnected && processView.mode==='builtin') workspace?.select([],task).catch(error=>{status.textContent=error.message;});
            return actual;
        };
        host.querySelector('[data-processes-builtin]').append(builtin);
        host.querySelector('[data-processes-custom]').append(custom);
        custom.addEventListener('honua-processes-layer-ready', async event => {
            try {
                const layer = event.detail?.layer;
                if (!Number.isInteger(layer?.layerId) || typeof layer.serviceName !== 'string')
                    throw new Error('The operation returned no verified published layer identity.');
                const app = studioElement(session);
                const outcome = await app.addPublishedLayer(layer);
                if (!outcome.ok) throw new Error(outcome.reason || 'The published layer could not be added to the map.');
                element = app;
                window.Blazor?.navigateTo('/studio');
            } catch (error) { status.textContent = error.message; }
        });
        status.textContent = `Connected to ${session.environmentName}`;
    } catch (error) { status.textContent = error.message; }
}
new MutationObserver(mountProcesses).observe(document.body, { childList: true, subtree: true });
mountProcesses();

// Refresh the host transport lease without revealing or storing the upstream bearer.
setInterval(async () => {
    if (!element?.isConnected) return;
    try {
        const response = await fetch('/api/agent-workspace/session', { credentials: 'same-origin' });
        if (!response.ok) return;
        const refreshed = await response.json();
        if (refreshed.environmentId !== session?.environmentId) {
            const status = document.querySelector('[data-studio-status]');
            if (status) status.textContent = 'The active environment changed. Reopen Studio to reconnect; this view retains its previous draft.';
            return;
        }
        session = refreshed; studioElement(session);
    } catch { /* Existing lease expires closed; reconnect remains explicit after transport failure. */ }
}, 120000);

window.addEventListener('honua-workspace-conversation', event => {
    if (element?.isConnected) element.setWorkspaceConversation?.(event.detail);
});

window.addEventListener('honua-agent-draft', async event => {
    const detail = event.detail;
    const app = elementsByEnvironment.get(detail.environmentId);
    if (!app || window.honuaAgentWorkspace?.workspaceId !== detail.workspaceId) return;
    try {
        await app.prepareWorkspaceTurn();
        const task = app.workspace?.context?.task;
        if (task?.kind === 'studio-composition' && task.id !== detail.draftId)
            throw new Error('The agent updated its captured draft. This view now targets another draft.');
        await app.attachWorkspaceDraft(detail.draftId);
    }
    catch (error) {
        const status = document.querySelector('[data-studio-status]');
        if (status) status.textContent = error.message;
    }
});

window.addEventListener('honua-workspace-studio-task', async event => {
    const task = event.detail;
    if (!element?.isConnected || element.workspace?.session?.environmentId !== session?.environmentId) return;
    if (element.workspace.capturing || element.workspace.agentSession?.busy) return;
    try {
        if (element.workspaceDraftIdentity?.draftId === task.id)
            await element.refreshWorkspaceDraft();
        else {
            await element.prepareWorkspaceTurn();
            await element.attachWorkspaceDraft(task.id);
        }
    }
    catch (error) {
        const status = document.querySelector('[data-studio-status]');
        if (status) status.textContent = error.message;
    }
});

function processTask(value) {
    if (!value || typeof value.packageId !== 'string' || !/^[A-Za-z0-9_-]{1,256}$/.test(value.packageId)
        || !Number.isInteger(value.version) || value.version < 1 || !/^[a-f0-9]{64}$/.test(value.contentHash || ''))
        throw new Error('Processes returned no verified immutable package identity.');
    return {kind:'workflow-package',id:value.packageId,revision:value.version,contentHash:value.contentHash};
}
function processContextPreparation() {
    const view=processView;
    const expected=window.honuaAgentWorkspace?.context?.task;
    return async()=> {
        if(!view?.host.isConnected || view.mode==='custom') return null;
        const task=processTask(await view.builtin.prepareWorkspaceTask());
        if(expected?.kind!=='workflow-package' || expected.id!==task.id || expected.revision!==task.revision || expected.contentHash!==task.contentHash)
            throw new Error('Prepare and review the current Processes package before submitting.');
        return task;
    };
}
window.addEventListener('honua-workspace-process-task',async event=>{
    const view=processView,workspace=window.honuaAgentWorkspace;
    if(!view?.host.isConnected || view.mode==='custom' || workspace?.capturing || workspace?.agentSession?.busy) return;
    const task=event.detail;
    try { await view.builtin.attachWorkspaceTask({packageId:task.id,version:task.revision,contentHash:task.contentHash}); }
    catch(error) { const status=view.host.querySelector('[data-processes-status]');if(status) status.textContent=error.message; }
});
