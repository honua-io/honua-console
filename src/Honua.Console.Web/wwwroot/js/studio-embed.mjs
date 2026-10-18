import { selectedResource, studioResourceRefs } from './agent-workspace.mjs';
let assets;
let element;
const elementsByEnvironment = new Map();
let session;
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
            const workspace = window.honuaAgentWorkspace;
            if (workspace?.session?.environmentId === session.environmentId) {
                workspace.agentSession?.attachDraft({draftId:event.detail.draftId,generation:event.detail.generation});
                if (workspace.context && event.detail.packageKey)
                    workspace.select(workspace.context.selectedResources || [], {
                        id:event.detail.draftId,revision:event.detail.generation,
                        kind:'studio-composition',packageKey:event.detail.packageKey
                    }).catch(error => {
                        const status=document.querySelector('[data-studio-status]');
                        if(status) status.textContent=error.message;
                    });
            }
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
        csrfToken: session.csrfToken, environmentId: session.environmentId };
    app.workspace = window.honuaAgentWorkspace;
    if (app.workspace) app.workspace.selectionProvider = () => app.isConnected
        ? studioResourceRefs(app.composition.selection, app.composition.state.layers, app.sourceCatalog)
        : selectedResource(location.pathname);
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
        const draft = element.workspace?.agentSession?.sdkSession?.draft;
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
    try { await app.attachWorkspaceDraft(detail.draftId); }
    catch (error) {
        const status = document.querySelector('[data-studio-status]');
        if (status) status.textContent = error.message;
    }
});

window.addEventListener('honua-workspace-studio-task', async event => {
    const task = event.detail;
    if (!element?.isConnected || element.workspace?.session?.environmentId !== session?.environmentId) return;
    const key = `${task.id}:${task.revision}`;
    if (element.workspaceTaskKey === key) return;
    try { await element.attachWorkspaceDraft(task.id); element.workspaceTaskKey = key; }
    catch (error) {
        const status = document.querySelector('[data-studio-status]');
        if (status) status.textContent = error.message;
    }
});
