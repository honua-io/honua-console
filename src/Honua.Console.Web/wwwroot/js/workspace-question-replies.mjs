// A reply is a new request explicitly linked to one retained native question.
// The server independently checks authority, retirement, costs and admission.
import {openStudioExecutionResult} from './workspace-result-navigation.mjs';
import {refusalSummary} from './workspace-executions.mjs';
const taskRoutes={'studio-composition':'/studio','publish-layer':'/operate/publishing/quick',
    'dataset-import':'/operate/resources/import/task','workflow-package':'/studio/processes',
    'local-process-workspace':'/studio/processes'};
function target(context) {
    if (!context?.workspaceId || !context.targetId || !Array.isArray(context.selectedResources))
        throw new Error('The original question context is unavailable. Check the request again.');
    return JSON.stringify([context.workspaceId,context.targetId,context.tenantId ?? null,
        context.selectedResources.map(resource=>[resource.kind,resource.id,resource.serviceId ?? null])
            .sort((a,b)=>JSON.stringify(a).localeCompare(JSON.stringify(b))),
        context.task ? [context.task.kind || 'publish-layer',context.task.id,context.task.packageKey ?? null] : null]);
}
const review = context => JSON.stringify([target(context),context.task?.revision ?? null,
    context.task?.contentHash ?? null,context.task?.impactHash ?? null]);

export class WorkspaceQuestionReplies {
    constructor(workspace) { this.workspace=workspace;this.binding=null; }
    current() {
        if (this.binding?.scope !== this.workspace.executions.scope()) this.binding=null;
        return this.binding;
    }
    async begin(turnId) {
        const scope=this.workspace.executions.scope();
        const original=await this.workspace.executions.original(turnId);
        if (scope !== this.workspace.executions.scope()) throw new Error('Reconnect before replying to the original question.');
        if (original.execution?.state !== 'awaiting-input' || !original.execution.question)
            throw new Error('This request is no longer awaiting an answer. Check its current progress.');
        this.binding={scope,turnId,executionId:original.execution.executionId,
            question:{...original.execution.question},context:structuredClone(original.context),
            reviewed:review(original.context) === review(this.workspace.context) ? review(this.workspace.context) : null};
        this.workspace.notify(this.workspace);
        return this.binding;
    }
    cancel() { this.binding=null;this.workspace.notify(this.workspace); }
    requireContext(binding) {
        if (this.current() !== binding) throw new Error('The reply question changed. Review it before sending.');
        const style=this.workspace.sharedStyleProvider?.();
        if (this.workspace.hasUnsavedWork() || style?.dirty || style?.pending)
            throw new Error('Save or discard your edits before replying. Your answer has been kept.');
        if (target(binding.context) !== target(this.workspace.context))
            throw new Error('Return to the original question target before replying. Your answer has been kept.');
        if (binding.context.task && (!Number.isSafeInteger(this.workspace.context.task?.revision)
            || this.workspace.context.task.revision < binding.context.task.revision))
            throw new Error('Read the latest original task before replying. Your answer has been kept.');
    }
    async observed(binding) {
        const original=await this.workspace.executions.original(binding.turnId);
        if (this.current() !== binding || original.execution?.executionId !== binding.executionId
            || original.execution.state !== 'awaiting-input'
            || JSON.stringify(original.execution.question) !== JSON.stringify(binding.question)
            || target(original.context) !== target(binding.context))
            throw new Error('The original question is no longer available for this reply. Check its progress.');
        return original;
    }
    async reviewCurrent() {
        const binding=this.current();
        if (!binding) throw new Error('Select the original question before replying.');
        await this.observed(binding);this.requireContext(binding);
        binding.reviewed=review(this.workspace.context);this.workspace.notify(this.workspace);
    }
    async prepare(binding) {
        this.requireContext(binding);
        const original=await this.observed(binding);this.requireContext(binding);
        if (original.execution.replyAdmission?.state === 'blocked')
            throw new Error(refusalSummary(original.execution.replyAdmission));
        if (binding.reviewed !== review(this.workspace.context))
            throw new Error('Review the current target before sending your answer. Your answer has been kept.');
        return {replyToQuestionId:binding.question.id,replyToTurnId:binding.turnId,replyToExecutionId:binding.executionId};
    }
    async openOriginal({navigate=path=>window.Blazor?.navigateTo ? window.Blazor.navigateTo(path) : window.location.assign(path),
        present=task=>window.dispatchEvent(new CustomEvent('honua-workspace-studio-task',{detail:task}))}={}) {
        const binding=this.current(),workspace=this.workspace,generation=workspace.contextGeneration;
        if (!binding || !taskRoutes[binding.context.task?.kind]) throw new Error('Return to the original selection shown with the question.');
        const current=()=>this.current() === binding && workspace.contextGeneration === generation;
        const check=()=>{
            const style=workspace.sharedStyleProvider?.();
            if (!current()) throw new Error('The selected context changed. Open the original task again.');
            if (workspace.hasUnsavedWork() || style?.dirty || style?.pending)
                throw new Error('Save or discard your edits before opening the original task. Your answer has been kept.');
            if (workspace.context?.targetId !== binding.context.targetId)
                throw new Error('Connect to the original target before opening its task.');
        };
        check();await this.observed(binding);check();await workspace.authorityChangeGuard?.();check();
        const task={...binding.context.task};
        if (task.kind === 'studio-composition') {
            const response=await workspace.request(`/api/studio/v1/studio/package-drafts/${encodeURIComponent(task.id)}`,{
                method:'GET',credentials:'same-origin',headers:{'X-Honua-Environment':workspace.session.environmentId,
                    'X-Honua-Session-Partition':workspace.session.principalId}});
            check();
            if (!response.ok) throw new Error('The original draft is unavailable with your current access.');
            const draft=(await response.json())?.data;check();
            if (draft?.draftId !== task.id || draft.packageKey !== task.packageKey
                || !['map','dashboard'].includes(draft.envelope?.family)
                || !Number.isSafeInteger(draft.generation) || draft.generation < task.revision)
                throw new Error('The original draft could not be verified.');
            await openStudioExecutionResult(workspace,{kind:'studio-composition',draftId:task.id,itemId:draft.itemId,
                generation:task.revision,family:draft.envelope.family},current,
                {navigate,present,selectedResources:binding.context.selectedResources});
        } else {
            if (task.kind === 'publish-layer') {
                const draft=await workspace.api(`${workspace.workspaceId}/drafts/${encodeURIComponent(task.id)}`);check();
                if (draft?.id !== task.id || draft.kind !== task.kind || !Number.isSafeInteger(draft.revision) || draft.revision < task.revision)
                    throw new Error('The original publication draft could not be verified.');
                await workspace.reconcileDraft(draft);check();
                task.revision=draft.revision;
            }
            // Import and process references identify immutable task revisions;
            // the existing editor/SDK reauthorizes that reference when opened.
            const selected=workspace.select(binding.context.selectedResources,task);
            const selectedGeneration=workspace.contextGeneration;await selected;
            if (this.current() !== binding || workspace.contextGeneration !== selectedGeneration
                || target(workspace.context) !== target(binding.context) || workspace.hasUnsavedWork())
                throw new Error('The selected context changed. Open the original task again.');
            navigate(taskRoutes[task.kind]);
        }
        binding.reviewed=null;workspace.notify(workspace);
    }
    accepted(binding) { if (this.binding === binding) this.binding=null; }
}

export function renderQuestionReply(host, workspace) {
    if (!host) return;
    const binding=workspace.questionReplies.current();
    const original=binding && workspace.executions.current().find(row=>row.turnId === binding.turnId);
    host.hidden=!binding;
    if (!binding) return;
    const text=host.querySelector('[data-workspace-reply-description]');
    const open=host.querySelector('[data-workspace-reply-open]');
    if (open) open.hidden=!taskRoutes[binding.context.task?.kind] || !original?.execution || Boolean(original.error);
    if (!original?.execution || original.error) {
        text.textContent='The original question cannot currently be read. Your typed answer is kept; check request progress before sending.';
        return;
    }
    const context=binding.context;
    const resources=context.selectedResources.map(resource=>`${resource.kind} ${resource.id}`).join(', ');
    text.textContent=`Replying to: ${binding.question.text} · Original target: ${context.targetId} · ${context.task?.id || resources || 'No resource selected'}`;
    if (original.execution.replyAdmission?.state === 'blocked')
        text.textContent+=` · ${refusalSummary(original.execution.replyAdmission)}`;
}
