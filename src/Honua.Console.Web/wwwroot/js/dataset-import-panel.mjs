import {DatasetImportController, datasetImportBinding, datasetImportKey} from './dataset-import-controller.mjs';
import {DatasetImportStore} from './dataset-import-store.mjs';
const store = new DatasetImportStore();
let mounted;
function mount() {
    const host = document.querySelector('[data-dataset-import-panel]');
    if (mounted?.host === host) return;
    mounted?.dispose(); mounted = null;
    if (!host) return;
    const lifetime = new AbortController(), controls = {};
    for (const name of ['status','destination','source','file','progress','check','transfer','recover','pause','cancel'])
        controls[name] = host.querySelector(`[data-import-${name}]`);
    let controller, workspace, binding, generation = 0;
    const status = message => { controls.status.textContent = message; };
    const current = () => host.isConnected && window.honuaAgentWorkspace === workspace &&
        datasetImportKey(datasetImportBinding(workspace) || {task:{}}) === datasetImportKey(binding);
    const render = state => {
        if (!current()) return;
        status(state.message);
        controls.destination.textContent = state.task ? `${state.task.destination.schema}.${state.task.destination.table}` : 'Not bound';
        controls.source.textContent = state.task ? `${state.task.source.fileName} · ${state.task.source.totalBytes.toLocaleString()} bytes` : 'Not bound';
        controls.progress.value = state.stage === 'hashing' ? state.progress || 0
            : state.record?.receipt ? state.record.receipt.acceptedBytes / state.record.receipt.totalBytes : 0;
        const allowed = Boolean(state.task && state.capability && state.receiptAvailable && !state.record?.pending);
        controls.file.disabled = state.busy || !allowed || state.task.source.kind !== 'browser-file';
        controls.check.disabled = state.busy || !allowed || !controls.file.files?.length;
        controls.transfer.disabled = state.busy || !allowed || state.stage === 'retained' || state.stage === 'cancelled' ||
            (!state.file && state.record?.receipt?.state !== 'sealed');
        controls.transfer.textContent = state.record?.receipt?.state === 'sealed' ? 'Retain verified source' : 'Upload and verify';
        controls.recover.disabled = state.busy;
        controls.pause.disabled = !state.busy;
        controls.cancel.disabled = state.busy || !allowed || state.record?.receipt?.state !== 'receiving';
    };
    async function connect(force = false) {
        const nextWorkspace = window.honuaAgentWorkspace, next = datasetImportBinding(nextWorkspace);
        if (!force && next && binding && datasetImportKey(next) === datasetImportKey(binding) && controller) return;
        controller?.dispose(); workspace?.presentationGuards?.delete(dirty); controller = null; workspace = nextWorkspace; binding = next; const epoch = ++generation;
        controls.file.value = ''; controls.destination.textContent = 'Not bound'; controls.source.textContent = 'Not bound';
        for (const name of ['file','check','transfer','recover','pause','cancel']) controls[name].disabled = true;
        if (!next) { status('Ask the agent to prepare an import task, then open it from this conversation.'); return; }
        status('Checking this deployment’s import transport…');
        try {
            const sdk = await import('/_studio/console.js');
            if (epoch !== generation || !current()) return;
            if (!sdk.DatasetImportTaskClient || !sdk.DatasetImportUploadClient || !sdk.hashDatasetImportFile)
                throw new Error('The pinned import transport is unavailable in this Console build.');
            const headers = async () => {
                if (!current()) throw new Error('Return to the original task before continuing.');
                const session = await workspace.api('session');
                if (!current() || session.environmentId !== next.environmentId || session.principalId !== next.principalId)
                    throw new Error('The original connection changed. Return to its task before continuing.');
                return {'X-CSRF-Token':session.csrfToken,'X-Honua-Environment':session.environmentId,'X-Honua-Session-Partition':session.principalId};
            };
            const relay = async (input, init) => {
                if (!current()) throw new Error('The original import task is no longer selected.');
                const url = new URL(input, location.origin), prefix = `/api/v1/agent/workspaces/${encodeURIComponent(next.workspaceId)}/`;
                if (url.origin !== location.origin || !url.pathname.startsWith(prefix)) throw new Error('Invalid import transport binding.');
                url.pathname = url.pathname.replace('/api/v1/agent/workspaces/', '/api/agent-workspace/');
                const response = await fetch(url, {...init, credentials:'same-origin', redirect:'error'});
                if (!current()) throw new Error('The original import task changed while checking its receipt.');
                return response;
            };
            const options = {baseUrl:location.origin,workspaceId:next.workspaceId,targetId:next.targetId,headers,fetch:relay};
            const uploads = new sdk.DatasetImportUploadClient(options);
            if (typeof uploads.reconcile !== 'function') throw new Error('Original-session recovery is unavailable in this Console build.');
            controller = new DatasetImportController({binding:next,tasks:new sdk.DatasetImportTaskClient(options),uploads,
                hashFile:sdk.hashDatasetImportFile,store,isCurrent:current,notify:render});
            workspace.presentationGuards ||= new Set(); workspace.presentationGuards.add(dirty);
            await controller.initialize();
        } catch (error) { if (epoch === generation && current()) status(error.message); }
    }
    const dirty = () => Boolean(controller?.dirty());
    const action = async work => {
        try { await work(); }
        catch (error) { if (controller && current()) { render(controller); status(error.message); } }
    };
    controls.file.addEventListener('change', () => { if (controller) { controller.file = null; render(controller); } }, {signal:lifetime.signal});
    controls.check.addEventListener('click', () => action(() => controller.verifyFile(controls.file.files[0])), {signal:lifetime.signal});
    controls.transfer.addEventListener('click', () => action(() => controller.transfer()), {signal:lifetime.signal});
    controls.recover.addEventListener('click', () => action(() => controller.recover()), {signal:lifetime.signal});
    controls.cancel.addEventListener('click', () => action(() => controller.cancel()), {signal:lifetime.signal});
    controls.pause.addEventListener('click', () => { controller?.dispose(); action(() => connect(true)); }, {signal:lifetime.signal});
    window.addEventListener('honua-workspace-state', () => { action(() => connect()); }, {signal:lifetime.signal});
    mounted = {host,dispose:()=>{++generation;controller?.dispose();workspace?.presentationGuards?.delete(dirty);lifetime.abort();}};
    action(() => connect());
}
if (typeof document !== 'undefined') {
    new MutationObserver(mount).observe(document.body, {childList:true,subtree:true}); mount();
}
