import {browserImportSelection, browserSelectionScope} from './dataset-import-selection.mjs';
import {DatasetImportController, datasetImportBinding, datasetImportKey} from './dataset-import-controller.mjs';
import {DatasetImportSubmissionController} from './dataset-import-submission.mjs';
import {DatasetImportStore} from './dataset-import-store.mjs';
const store = new DatasetImportStore();
let mounted;
function mount() {
    const host = document.querySelector('[data-dataset-import-panel]');
    if (mounted?.host === host) return;
    mounted?.dispose(); mounted = null;
    if (!host) return;
    const lifetime = new AbortController(), controls = {};
    for (const name of ['status','destination','source','file','progress','check','clear','transfer','submit','recover','pause','cancel'])
        controls[name] = host.querySelector(`[data-import-${name}]`);
    let controller, submission, workspace, binding, generation = 0, prepareScope, preparing = false, selectionAbort, connecting = false;
    const sdk = () => import('/_studio/console.js');
    const status = message => { if (controls.status.textContent !== message) controls.status.textContent = message; };
    const errorMessage = error => error instanceof TypeError ? 'This import action could not finish. Check the original status before continuing.' : error?.message || 'Check the original import status before continuing.';
    const current = () => host.isConnected && window.honuaAgentWorkspace === workspace &&
        (binding ? datasetImportKey(datasetImportBinding(workspace) || {task:{}}) === datasetImportKey(binding)
            : JSON.stringify(browserSelectionScope(workspace)) === JSON.stringify(prepareScope));
    const renderSubmission = () => {
        if (!current() || !submission) return false;
        const receipt = submission.record?.receipt;
        const blocked = Boolean(submission.busy || submission.record?.pending || submission.available === false || receipt?.state === 'prepared' || receipt?.state === 'accepted');
        controls.submit.disabled = submission.busy || blocked || !controller?.record?.receipt?.sourceRetention ||
            controller?.capability?.importAcceptanceSupported !== true;
        if (!blocked) return false;
        status(submission.message());
        controls.clear.disabled = submission.busy || (!controller?.file && !browserImportSelection.record);
        for (const name of ['file','check','transfer','submit','cancel']) controls[name].disabled = true;
        controls.recover.disabled = submission.busy;
        controls.pause.disabled = !submission.busy;
        if (receipt?.state === 'accepted' && receipt.job?.output) controls.destination.textContent = `${receipt.job.output.schema}.${receipt.job.output.physicalTableName}`;
        return true;
    };
    const render = state => {
        if (!current()) return;
        if (renderSubmission()) return;
        status(state.message);
        controls.destination.textContent = state.task ? `${state.task.destination.schema}.${state.task.destination.table}` : 'Not bound';
        controls.source.textContent = state.task ? `${state.task.source.fileName} · ${state.task.source.totalBytes.toLocaleString()} bytes` : 'Not bound';
        controls.progress.value = state.stage === 'hashing' ? state.progress || 0
            : state.record?.receipt ? state.record.receipt.acceptedBytes / state.record.receipt.totalBytes : 0;
        const allowed = Boolean(state.task && state.capability && state.receiptAvailable && !state.record?.pending);
        controls.file.disabled = state.busy || !allowed || state.task.source.kind !== 'browser-file';
        controls.check.disabled = state.busy || !allowed || !controls.file.files?.length;
        controls.clear.disabled = state.busy || !state.file;
        controls.transfer.disabled = state.busy || !allowed || state.stage === 'retained' || state.stage === 'cancelled' ||
            (!state.file && state.record?.receipt?.state !== 'sealed');
        controls.transfer.textContent = state.record?.receipt?.state === 'sealed' ? 'Retain verified source' : 'Upload and verify';
        controls.recover.disabled = state.busy;
        controls.pause.disabled = !state.busy;
        controls.cancel.disabled = state.busy || !allowed || state.record?.receipt?.state !== 'receiving';
        controls.submit.disabled = state.busy || submission?.busy || !submission || !allowed || !state.record?.receipt?.sourceRetention || state.capability.importAcceptanceSupported !== true;
    };
    async function connect(force = false) {
        const nextWorkspace = window.honuaAgentWorkspace, next = datasetImportBinding(nextWorkspace);
        const nextScope = browserSelectionScope(nextWorkspace);
        if (!force && workspace === nextWorkspace && next && binding && datasetImportKey(next) === datasetImportKey(binding) && (connecting || controller || submission)) {
            if (!connecting && submission && !submission.busy && (submission.record?.pending ||
                ['prepared','accepted'].includes(submission.record?.receipt?.state))) {
                await submission.recover({background:true}); renderSubmission();
            }
            return;
        }
        if (!force && !next && !binding && workspace === nextWorkspace && prepareScope && JSON.stringify(prepareScope) === JSON.stringify(nextScope)) return;
        controller?.dispose(); submission?.dispose(); submission = null; selectionAbort?.abort(); preparing = false; workspace?.presentationGuards?.delete(dirty); controller = null; workspace = nextWorkspace; binding = next; prepareScope = nextScope; const epoch = ++generation;
        controls.file.value = ''; controls.destination.textContent = 'Not bound'; controls.source.textContent = 'Not bound';
        for (const name of ['file','check','clear','transfer','submit','recover','pause','cancel']) controls[name].disabled = true;
        if (!next) {
            if (!prepareScope) { status('Connect this conversation before choosing an import file.'); return; }
            workspace.presentationGuards ||= new Set(); workspace.presentationGuards.add(dirty);
            controls.file.disabled = false;
            if (browserImportSelection.record) {
                const selected = browserImportSelection.record;
                controls.source.textContent = `${selected.fileName} · ${selected.totalBytes.toLocaleString()} bytes`;
                controls.clear.disabled = false;
            }
            status('Choose your dataset and check the complete file. Then describe its destination in this conversation.');
            return;
        }
        connecting = true; status('Checking this deployment’s import transport…');
        try {
            const module = await sdk();
            if (epoch !== generation || !current()) return;
            if (!module.DatasetImportTaskClient || !module.DatasetImportUploadClient || !module.hashDatasetImportFile)
                throw new Error('The pinned import transport is unavailable in this Console build.');
            const headers = async () => {
                if (!current()) throw new Error('Return to the original task before continuing.');
                const session = await workspace.api('session');
                if (!current() || session.environmentId !== next.environmentId || session.principalId !== next.principalId ||
                    (session.releaseId ?? null) !== (next.releaseId ?? null) || (session.sourcePathBase ?? null) !== (next.sourcePathBase ?? null))
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
            if (module.DatasetImportSubmissionClient) {
                submission = new DatasetImportSubmissionController({binding:next, submissions:new module.DatasetImportSubmissionClient(options), store,
                    isCurrent:current, notify:()=>renderSubmission()});
                workspace.presentationGuards ||= new Set(); workspace.presentationGuards.add(dirty);
                await submission.recover();
                if (epoch !== generation || !current() || renderSubmission()) return;
            }
            const uploads = new module.DatasetImportUploadClient(options);
            if (typeof uploads.reconcile !== 'function') throw new Error('Original-session recovery is unavailable in this Console build.');
            controller = new DatasetImportController({binding:next,tasks:new module.DatasetImportTaskClient(options),uploads,
                hashFile:module.hashDatasetImportFile,store,isCurrent:current,notify:render});
            workspace.presentationGuards ||= new Set(); workspace.presentationGuards.add(dirty);
            await controller.initialize();
            const originalFile = browserImportSelection.matchingFile(workspace, controller);
            if (originalFile && !controller.record?.receipt?.sourceRetention) await controller.verifyFile(originalFile);
        } catch (error) { if (epoch === generation && current()) status(errorMessage(error)); }
        finally { if (epoch === generation) connecting = false; }
    }
    const dirty = () => preparing || Boolean(submission?.dirty()) || Boolean(controller?.dirty()) || Boolean(!controller && browserImportSelection.record && !browserImportSelection.record.turnId);
    const action = async work => {
        const epoch = generation;
        try { await work(); }
        catch (error) { if (epoch === generation && current()) { if (controller) render(controller); status(errorMessage(error)); } }
    };
    controls.file.addEventListener('change', () => { if (controller) { controller.file = null; render(controller); } else { browserImportSelection.clear(); controls.check.disabled = !controls.file.files?.length; controls.clear.disabled = false; } }, {signal:lifetime.signal});
    controls.check.addEventListener('click', () => action(async () => {
        if (controller) return controller.verifyFile(controls.file.files[0]);
        if (preparing || !current()) return;
        const selectedFile = controls.file.files[0], selectedWorkspace = workspace, epoch = generation;
        const requestAbort = new AbortController();
        preparing = true; selectionAbort = requestAbort; controls.check.disabled = true; controls.file.disabled = true; controls.pause.disabled = false;
        try {
            const module = await sdk(); if (epoch !== generation || requestAbort.signal.aborted || !current()) return;
            if (!module.hashDatasetImportFile) throw new Error('The pinned complete-file checker is unavailable in this Console build.');
            const selected = await browserImportSelection.prepare(selectedFile, selectedWorkspace, {hashFile:module.hashDatasetImportFile,signal:requestAbort.signal,
                onProgress:(bytes,total)=>{if(epoch === generation && !requestAbort.signal.aborted && current()){controls.progress.value=bytes/total;status('Checking the complete file…');}}});
            if (epoch !== generation || requestAbort.signal.aborted || !current()) return;
            controls.source.textContent = `${selected.fileName} · ${selected.totalBytes.toLocaleString()} bytes`;
            status('File checked. Describe the destination in the shared conversation; your next message will capture this exact selection.');
            const conversation = document.querySelector('[data-agent-workspace]'); if (conversation) conversation.open = true;
            document.querySelector('[data-workspace-prompt]')?.focus();
        } finally { if(epoch === generation && selectionAbort === requestAbort){preparing = false;if(current()){controls.file.disabled=false;controls.check.disabled=!controls.file.files?.length;controls.pause.disabled=true;controls.clear.disabled=false;}} }
    }), {signal:lifetime.signal});
    controls.clear.addEventListener('click', () => { browserImportSelection.clear(); controls.file.value = ''; if(controller){controller.file=null;controller.present();}else{controls.check.disabled=true;controls.clear.disabled=true;status('Selected file cleared. Choose a dataset to prepare another import.');} }, {signal:lifetime.signal});
    controls.transfer.addEventListener('click', () => action(() => controller.transfer()), {signal:lifetime.signal});
    controls.submit.addEventListener('click', () => action(async () => {
        if (!controller || !submission || controller.capability?.importAcceptanceSupported !== true || !controller.record?.receipt?.sourceRetention) return;
        const review = {...controller.record.authority, taskId:binding.task.id, taskRevision:binding.task.revision, taskContentHash:binding.task.contentHash};
        await submission.prepare(review); renderSubmission();
    }), {signal:lifetime.signal});
    controls.recover.addEventListener('click', () => action(async () => {
        if (submission) { await submission.recover(); if (renderSubmission()) return; }
        await controller?.recover();
    }), {signal:lifetime.signal});
    controls.cancel.addEventListener('click', () => action(() => controller.cancel()), {signal:lifetime.signal});
    controls.pause.addEventListener('click', () => { controller?.dispose(); selectionAbort?.abort(); action(() => connect(true)); }, {signal:lifetime.signal});
    window.addEventListener('honua-workspace-state', () => { action(() => connect()); }, {signal:lifetime.signal});
    mounted = {host,dispose:()=>{++generation;controller?.dispose();submission?.dispose();selectionAbort?.abort();workspace?.presentationGuards?.delete(dirty);lifetime.abort();}};
    action(() => connect());
}
if (typeof document !== 'undefined') {
    new MutationObserver(mount).observe(document.body, {childList:true,subtree:true}); mount();
}
