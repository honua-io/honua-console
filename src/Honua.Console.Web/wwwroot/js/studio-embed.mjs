import {processTask} from './workflow-task.mjs';
import { capturedProcessFetch, createStudioCookieSession, sameCookieAuthority } from './cookie-workspace-transport.mjs';
import { captureWorkspaceFollow, queueWorkspaceFollow } from './workspace-follow.mjs';
import { selectedResource, studioResourceRefs } from './agent-workspace.mjs';
import { mountStudioVersionControls } from './studio-version-controls.mjs';
import { mountStudioStyleControls } from './studio-style-controls.mjs';
import { captureStudioDesignFocus } from './studio-design-focus.mjs';
let assets;
let element;
const elementsByEnvironment = new Map();
let session;
let processView;
// A retained visual buffer must be discarded through its authorized reader before detaching.
globalThis.honuaStudioStyleContext = () => element ? {app:element,binding:JSON.stringify(element.workspaceCookieBinding)} : undefined;
window.addEventListener('beforeunload',event=>{
    if(element?.hasUnsavedStyleChanges) {event.preventDefault();event.returnValue='';}
});
async function studioElement(session) {
    let app = elementsByEnvironment.get(session.environmentId);
    if (!app) {
        app = document.createElement('honua-studio-app');
        app.styleEditorAssetsUrl = '/_studio/maputnik/index.html';
        app.routingMode = 'host'; app.setAttribute('base-path', '/studio');
        app.addEventListener('honua-studio-navigate', event => {
            if (/^\/studio(?:\/[A-Za-z0-9_-]+)*$/.test(event.detail?.path || ''))
                window.Blazor?.navigateTo(event.detail.path);
        });
        app.addEventListener('honua-workspace-draft-change', event => {
            const detachedDraft=app.workspaceDraftIdentity;
            app.workspaceDraftIdentity = event.detail;
            app.workspaceTaskKey = event.detail ? `${event.detail.draftId}:${event.detail.generation}` : undefined;
            const workspace = window.honuaAgentWorkspace;
            if (workspace?.session?.environmentId === session.environmentId) {
                workspace.agentSession?.attachDraft(event.detail ? {draftId:event.detail.draftId,generation:event.detail.generation} : undefined);
                if (!event.detail) {
                    const staleAuthority=app.workspaceAuthorityChanged && workspace.session.principalId !== app.workspaceCookieBinding?.principalId;
                    if(staleAuthority) {
                        workspace.agentSession?.sdkSession?.close();workspace.agentSession=null;
                    }
                    if(staleAuthority || (app.isConnected && !app.workspaceAuthorityChanged && detachedDraft && detachedDraft.draftId === workspace.context?.task?.id && workspace.context?.task?.kind === 'studio-composition')) workspace.select(undefined,null).catch(()=>{});
                    return;
                }
                if (app.isConnected && workspace.context && event.detail.packageKey)
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
                }).finally(() => workspace.notify(workspace));
        });
        elementsByEnvironment.set(session.environmentId, app);
    }
    const previous=app.workspaceCookieBinding;
    const requestGeneration = (app.workspaceBindingRequest || 0) + 1;
    app.workspaceBindingRequest = requestGeneration;
    const changedAuthority=Boolean(previous && (previous.principalId !== session.principalId || previous.environmentId !== session.environmentId || previous.sourcePathBase !== session.sourcePathBase));
    if (changedAuthority) await app.prepareWorkspaceTurn();
    if (app.workspaceBindingRequest !== requestGeneration) throw new Error('A newer Studio connection replaced this request.');
    const cookieSession = createStudioCookieSession(session);
    app.workspaceAuthorityChanged=changedAuthority;
    app.workspaceCookieBinding={principalId:session.principalId,environmentId:session.environmentId,sourcePathBase:session.sourcePathBase};
    app.session = cookieSession;
    app.workspace = window.honuaAgentWorkspace;
    if (app.workspace) app.workspace.authorityChangeGuard = () => app.prepareWorkspaceTurn();
    if (app.workspace) app.workspace.studioDesignFocusProvider = () => app.isConnected
        && location.pathname === '/studio' ? captureStudioDesignFocus(app) : undefined;
    if (app.workspace) app.workspace.clearStudioDesignFocus = () => {
        if (app.isConnected && location.pathname === '/studio') app.composition.select([]);
    };
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
    app.workspaceAgentAdapter ||= { submitTurn: (prompt, options) => window.honuaAgentWorkspace.submit(prompt, options) };
    app.agentSessionAdapter = app.workspaceAgentAdapter;
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
        element = await studioElement(session);
        element.currentPath = location.pathname;
        host.querySelector('[data-studio-mount]').append(element);
        const createApp=element;
        const create=document.createElement('form');
        const key=document.createElement('input');key.required=true;key.maxLength=200;key.pattern='(?:[A-Za-z0-9._]|-){1,200}';
        key.placeholder='Map package key';key.setAttribute('aria-label','New map package key');
        const button=document.createElement('button');button.type='submit';button.textContent='New map';
        create.append(key,button);host.querySelector('[data-studio-mount]').before(create);
        create.addEventListener('submit',async event=>{
            event.preventDefault();if(!create.reportValidity()) return;button.disabled=true;
            try { await createApp.createWorkspaceDraft(key.value.trim());status.textContent='Shared server draft opened.'; }
            catch(error) {status.textContent=error.message;} finally {button.disabled=false;}
        });
        createApp.workspaceVersionControls?.dispose();
        createApp.workspaceVersionControls = mountStudioVersionControls({ app:createApp, host, status });
        createApp.workspaceStyleControls?.dispose();
        createApp.workspaceStyleControls = mountStudioStyleControls({ app:createApp, host, status });
        const task = element.workspace?.context?.task;
        const draft = task?.kind === 'studio-composition' ? {draftId:task.id}
            : !task ? element.workspace?.agentSession?.sdkSession?.draft : undefined;
        if (draft?.draftId) await element.attachWorkspaceDraft(draft.draftId);
        else if(element.workspaceDraftIdentity) {
            await element.prepareWorkspaceTurn();
            await element.refreshWorkspaceDraft();
            const visible=element.workspaceDraftIdentity;
            await element.workspace.select(undefined,{kind:'studio-composition',id:visible.draftId,revision:visible.generation,packageKey:visible.packageKey});
        }
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
        let view;
        const options = { environmentId: session.environmentId, csrfToken: session.csrfToken, principalId:session.principalId,
            fetchImpl:capturedProcessFetch(session,{current:()=>view?.session || session}) };
        const builtin = document.createElement('honua-studio-processes');
        builtin.client = new module.ServerProcessWorkspace(options);
        const custom = document.createElement('honua-studio-custom-processes');
        custom.client = new module.LocalProcessWorkspace({...options,maxArtifactUploadBytes:session.maxArtifactUploadBytes,workspaceId:()=>window.honuaAgentWorkspace?.workspaceId});
        view = processView = {builtin,custom,host,mode:'builtin',session};
        const sourceApp=await studioElement(session);
        const pickerApp=async()=>{
            const response=await fetch('/api/agent-workspace/session',{credentials:'same-origin'});
            if(!response.ok) throw new Error('Sign in again before choosing a process source.');
            const renewed=await response.json();
            if(processView!==view || !host.isConnected || view.mode!=='builtin' || !sameCookieAuthority(session,renewed))
                throw new Error('The process view or connection changed. Review it before choosing a source.');
            const app=await studioElement(renewed);
            if(processView!==view || !host.isConnected || view.mode!=='builtin') throw new Error('The process view changed.');
            view.session=renewed;return app;
        };
        if(typeof sourceApp.listProcessSources==='function' && typeof sourceApp.resolveProcessSource==='function')
            builtin.sourcePicker={list:async()=> (await pickerApp()).listProcessSources(),resolve:async id=>(await pickerApp()).resolveProcessSource(id)};
        const workspace=window.honuaAgentWorkspace;
        if(workspace) {
            workspace.prepareContextProvider=()=> location.pathname === '/studio/processes' ? processContextPreparation() : undefined;
            if(workspace.context?.task?.kind === 'workflow-package') {
                const task=workspace.context.task;
                const current=captureWorkspaceFollow(workspace,task,()=>processView===view && host.isConnected && view.mode==='builtin');
                if(await builtin.attachWorkspaceTask({packageId:task.id,version:task.revision,contentHash:task.contentHash}) && current()) {
                    view.builtinIdentity={packageId:task.id,version:task.revision,contentHash:task.contentHash};
                    await workspace.select([],task);
                }
            } else if(workspace.context?.task?.kind === 'local-process-workspace') {
                const task=workspace.context.task;view.mode='custom';
                const current=captureWorkspaceFollow(workspace,task,()=>processView===view && host.isConnected && view.mode==='custom');
                if(await custom.attachWorkspaceTask({projectId:task.id,revision:task.revision,sourceDigest:task.contentHash}) && current()) {
                    view.localIdentity={projectId:task.id,revision:task.revision,sourceDigest:task.contentHash};view.localProjectId=task.id;
                    await workspace.select([],task);
                }
            } else await workspace.select([],null);
        }
        if(processView!==view || !host.isConnected || workspace?.session?.principalId!==session.principalId || workspace?.session?.environmentId!==session.environmentId) return;
        custom.addEventListener('focusin',()=>{
            if(!host.isConnected || processView!==view) return;
            view.mode='custom';
            workspace?.select([],view.localIdentity ? localProcessTask(view.localIdentity) : null).catch(error=>{status.textContent=error.message;});
        });
        custom.addEventListener('honua-local-process-task-change',event=>{
            view.localIdentity=event.detail;
            if(event.detail) view.localProjectId=event.detail.projectId;
            if(host.isConnected && processView===view && view.mode==='custom' && !workspace?.capturing) workspace?.select([],event.detail ? localProcessTask(event.detail) : null).catch(error=>{status.textContent=error.message;});
        });
        builtin.addEventListener('focusin',()=>{view.mode='builtin';workspace?.select([],view.builtinIdentity ? processTask(view.builtinIdentity) : null).catch(error=>{status.textContent=error.message;});});
        builtin.addEventListener('input',()=>{view.builtinIdentity=undefined;workspace?.select(undefined,null).catch(()=>{});});
        const prepare=builtin.client.prepare.bind(builtin.client);
        builtin.client.prepare=async(...args)=>{
            const actual=await prepare(...args);
            const task=processTask(actual);
            view.builtinIdentity=actual;
            if(host.isConnected && view.mode==='builtin') workspace?.select([],task).catch(error=>{status.textContent=error.message;});
            return actual;
        };
        host.querySelector('[data-processes-builtin]').append(builtin);
        host.querySelector('[data-processes-custom]').append(custom);
        custom.addEventListener('honua-processes-layer-ready', async event => {
            try {
                if(!host.isConnected || window.honuaAgentWorkspace?.session?.environmentId !== session.environmentId) return;
                const layer = event.detail?.layer;
                if (!Number.isInteger(layer?.layerId) || typeof layer.serviceName !== 'string')
                    throw new Error('The operation returned no verified published layer identity.');
                const app = await studioElement(session);
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

// Refresh only an identical captured authority; long-lived Processes clients keep their editor state.
setInterval(async () => {
    const view=processView?.host.isConnected ? processView : undefined;
    const app=element?.isConnected ? element : undefined;
    const sharedEditor=document.querySelector('[data-shared-style-editor]');
    const sharedBinding=sharedEditor?.dataset.styleAuthorityBinding;
    const retained=sharedBinding && element?.workspaceCookieBinding?.principalId === sharedBinding ? element : undefined;
    if(!view && !app && !retained) return;
    try {
        const response=await fetch('/api/agent-workspace/session',{credentials:'same-origin'});
        if(!response.ok) return;
        const refreshed=await response.json(),bound=view?.session || session;
        if(!sameCookieAuthority(bound,refreshed)) {
            const status=document.querySelector(view ? '[data-processes-status]' : '[data-studio-status]');
            if(status) status.textContent='The connection changed. Save your work and reopen this view to reconnect.';
            return;
        }
        if(view && (processView!==view || !view.host.isConnected)) return;
        if(retained && (document.querySelector('[data-shared-style-editor]')!==sharedEditor || sharedEditor.dataset.styleAuthorityBinding!==sharedBinding || element!==retained)) return;
        if(app || retained || elementsByEnvironment.has(refreshed.environmentId)) await studioElement(refreshed);
        if(view) view.session=refreshed;
        session=refreshed;
    } catch { /* Expired leases fail closed; no automatic authority replacement. */ }
},120000);

window.addEventListener('honua-workspace-conversation', event => {
    if (element?.isConnected) element.setWorkspaceConversation?.(event.detail);
});

// Serialize observations per retained editor. A newer task invalidates queued work;
// the element also checks its own attachment epoch while its REST read is pending.
function followEditor(owner, workspace, task, isCurrentView, observe, onError) {
    return queueWorkspaceFollow(owner,workspace,task,isCurrentView,observe,onError);
}

function studioFollow(app, workspace, task, observe) {
    followEditor(app,workspace,task,()=>app.isConnected && app===element && app.workspace===workspace,
        observe,error=>{const status=document.querySelector('[data-studio-status]');if(status) status.textContent=error.message;});
}
window.addEventListener('honua-agent-draft', event => {
    const detail=event.detail,workspace=window.honuaAgentWorkspace;
    const app=elementsByEnvironment.get(detail.environmentId),task=workspace?.context?.task;
    if(!app || workspace?.workspaceId!==detail.workspaceId || task?.kind!=='studio-composition' || task.id!==detail.draftId) return;
    studioFollow(app,workspace,task,async current=>{
        await app.prepareWorkspaceTurn();
        if(current()) await app.attachWorkspaceDraft(detail.draftId);
    });
});
window.addEventListener('honua-workspace-studio-task', event => {
    const app=element,workspace=app?.workspace,task=event.detail;
    if(!app?.isConnected || workspace?.capturing || workspace?.agentSession?.busy) return;
    studioFollow(app,workspace,task,async current=>{
        if(app.workspaceDraftIdentity?.draftId===task.id) await app.refreshWorkspaceDraft();
        else { await app.prepareWorkspaceTurn();if(current()) await app.attachWorkspaceDraft(task.id); }
    });
});

function processContextPreparation() {
    const view=processView;
    const expected=window.honuaAgentWorkspace?.context?.task;
    const mode=view?.mode,projectId=view?.localProjectId;
    const requireSameView=()=>{
        const current=window.honuaAgentWorkspace?.session;
        if(processView!==view || !view?.host.isConnected || view.mode!==mode || location.pathname!=='/studio/processes'
            || current?.environmentId!==view.session.environmentId || current?.principalId!==view.session.principalId || current?.sourcePathBase!==view.session.sourcePathBase)
            throw new Error('The process view changed while submitting. Review the current view before retrying.');
    };
    return async()=> {
        requireSameView();
        if(mode==='custom') {
            const task=localProcessTask(await view.custom.prepareWorkspaceTask());
            requireSameView();
            if(!projectId || projectId!==task.id || view.localProjectId!==projectId) throw new Error('Load and review the current process project before submitting.');
            return task;
        }
        const task=processTask(await view.builtin.prepareWorkspaceTask());
        requireSameView();
        if(expected?.kind!=='workflow-package' || expected.id!==task.id || expected.revision!==task.revision || expected.contentHash!==task.contentHash)
            throw new Error('Prepare and review the current Processes package before submitting.');
        return task;
    };
}
window.addEventListener('honua-workspace-process-task',event=>{
    const view=processView,workspace=window.honuaAgentWorkspace,task=event.detail;
    if(!view?.host.isConnected || view.mode==='custom' || workspace?.capturing || workspace?.agentSession?.busy) return;
    followEditor(view,workspace,task,()=>processView===view && view.host.isConnected && view.mode==='builtin',
        async current=>{if(current()) await view.builtin.attachWorkspaceTask({packageId:task.id,version:task.revision,contentHash:task.contentHash});},
        error=>{const status=view.host.querySelector('[data-processes-status]');if(status) status.textContent=error.message;});
});

function localProcessTask(value) {
    if(!value || !/^[a-f0-9]{32}$/.test(value.projectId || '') || !Number.isInteger(value.revision) || value.revision < 1 || !/^[a-f0-9]{64}$/.test(value.sourceDigest || ''))
        throw new Error('The process workspace returned no verified project identity.');
    return {kind:'local-process-workspace',id:value.projectId,revision:value.revision,contentHash:value.sourceDigest};
}
window.addEventListener('honua-workspace-local-process-task',event=>{
    const view=processView,workspace=window.honuaAgentWorkspace,task=event.detail;
    if(!view?.host.isConnected || view.mode!=='custom' || workspace?.capturing || workspace?.agentSession?.busy) return;
    followEditor(view,workspace,task,()=>processView===view && view.host.isConnected && view.mode==='custom',async current=>{
        if(!current()) return;
        if(view.localIdentity?.projectId===task.id) await view.custom.refreshWorkspaceTask();
        else await view.custom.attachWorkspaceTask({projectId:task.id,revision:task.revision,sourceDigest:task.contentHash});
    },error=>{const status=view.host.querySelector('[data-processes-status]');if(status) status.textContent=error.message;});
});
