// Native execution observations are separate from model conversation text. This
// reader never launches a worker, replays a prompt or approves a proposal.
const identity = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(value);
const guid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const states = new Set(['queued', 'claimed', 'running', 'awaiting-input', 'awaiting-approval', 'interrupted', 'completed', 'failed']);
const labels = { queued:'Waiting for agent', claimed:'Starting', running:'Working',
    'awaiting-input':'Awaiting your answer', 'awaiting-approval':'Awaiting approval', interrupted:'Interrupted', completed:'Completed', failed:'Failed' };
const timestamp = value => typeof value === 'string' && Number.isFinite(Date.parse(value));
const optionalIdentity = value => value === undefined || value === null || identity(value);
const operationName = value => value == null || typeof value === 'string' && /^[A-Za-z0-9._:-]{1,128}$/.test(value);
const digest = value => typeof value === 'string' && /^sha256:[0-9a-f]{64}$/.test(value);
const refusalMessages={
    'context-changed':'Your request was not started. Review the current selection, then send again.',
    'reply-target-changed':'Return to the original question target, review it, then send your answer again.',
    'reply-question-changed':'The original question changed. Check request progress before sending your answer again.',
    'execution-retirement-required':'Previous work is still finishing. Check request progress before sending your answer again.',
    'author-authority-denied':'Your access to the original task could not be confirmed. Reconnect and check access before sending again.',
    'runtime-unavailable':'The agent is currently unavailable. Check request progress before sending again.'
};

export async function submissionDigest(serialized) {
    const bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(serialized));
    return `sha256:${Array.from(new Uint8Array(bytes),byte=>byte.toString(16).padStart(2,'0')).join('')}`;
}

function readSubmission(value, record, execution) {
    if (value == null) return null;
    if (value.turnId !== record.turnId || !digest(value.requestDigest)
        || record.requestDigest && value.requestDigest !== record.requestDigest
        || !['accepted','not-dispatched'].includes(value.disposition))
        throw new Error('The submission receipt does not match your original message. Check the request again.');
    if (value.disposition === 'not-dispatched') {
        if (!record.requestDigest || execution || !Object.hasOwn(refusalMessages,value.code))
            throw new Error('The refusal of your original request could not be verified. Check it again.');
        return {turnId:value.turnId,requestDigest:value.requestDigest,disposition:value.disposition,code:value.code};
    }
    if (!identity(value.executionId) || execution?.executionId !== value.executionId)
        throw new Error('The accepted execution does not match your original message. Check the request again.');
    return {turnId:value.turnId,requestDigest:value.requestDigest,disposition:value.disposition,executionId:value.executionId};
}

export function refusalSummary(submission) {
    return refusalMessages[submission.code] || 'Your request was not started. Review its target and permissions, then send again.';
}

export function readExecution(value) {
    if (!value || value.schemaVersion !== 1 || !identity(value.executionId) || !states.has(value.state)
        || !Number.isSafeInteger(value.stateRevision) || value.stateRevision < 0
        || !timestamp(value.createdAt) || !timestamp(value.updatedAt)
        || value.claimExpiresAt != null && !timestamp(value.claimExpiresAt)
        || !optionalIdentity(value.finalMessageId)
        || !Array.isArray(value.operations) || value.operations.length > 128
        || !Array.isArray(value.artifacts) || value.artifacts.length > 128)
        throw new Error('Execution status is unavailable. Reconnect to read the original request.');
    let question;
    if (value.question != null) {
        const candidate=value.question;
        if (!identity(candidate.id)
            || !['providerRequestId','providerToolCallId'].every(key=>typeof candidate[key] === 'string'
                && candidate[key].trim() && candidate[key].length <= 512)
            || typeof candidate.text !== 'string' || !candidate.text.trim() || candidate.text.length > 16000
            || typeof candidate.contextDigest !== 'string' || !/^(?:sha256:)?[0-9a-f]{64}$/i.test(candidate.contextDigest))
            throw new Error('The agent question could not be verified. Check the original request.');
        question={id:candidate.id,providerRequestId:candidate.providerRequestId,
            providerToolCallId:candidate.providerToolCallId,text:candidate.text,contextDigest:candidate.contextDigest};
    }
    if (value.state === 'awaiting-input' && !question)
        throw new Error('The agent question is unavailable. Check the original request.');
    let replyAdmission;
    if (value.replyAdmission != null) {
        const admission=value.replyAdmission;
        if (!question || !['ready','blocked'].includes(admission.state)
            || admission.state === 'ready' && admission.code != null
            || admission.state === 'blocked' && !['execution-retirement-required','author-authority-denied','runtime-unavailable'].includes(admission.code))
            throw new Error('Reply availability could not be verified. Check the original request.');
        replyAdmission={state:admission.state,...(admission.code ? {code:admission.code} : {})};
    }
    const operations = value.operations.map(operation => {
        if (!operation || !['operation-proposal','dataset-import','managed-job'].includes(operation.kind)
            || !identity(operation.id) || !operationName(operation.operationId)
            || !optionalIdentity(operation.proposalId) || !optionalIdentity(operation.jobId)
            || typeof operation.status !== 'string' || !operation.status || operation.status.length > 128)
            throw new Error('An operation reference could not be verified. Check the original request.');
        return {kind:operation.kind,id:operation.id,status:operation.status,
            ...(operation.operationId ? {operationId:operation.operationId} : {}),
            ...(operation.proposalId ? {proposalId:operation.proposalId} : {}),
            ...(operation.jobId ? {jobId:operation.jobId} : {})};
    });
    const artifacts = value.artifacts.map(artifact => {
        if (!artifact || artifact.kind !== 'studio-composition' || !guid(artifact.itemId)
            || !['map','dashboard'].includes(artifact.family)
            || artifact.draftId != null && !guid(artifact.draftId)
            || artifact.draftId != null && (!Number.isSafeInteger(artifact.generation) || artifact.generation < 0)
            || artifact.versionId != null && !guid(artifact.versionId)
            || artifact.contentHash != null && (typeof artifact.contentHash !== 'string'
                || !/^(?:sha256:)?[0-9a-f]{64}$/i.test(artifact.contentHash)))
            throw new Error('A result reference could not be verified. Check the original request.');
        return {kind:artifact.kind,itemId:artifact.itemId,family:artifact.family,
            ...(artifact.draftId ? {draftId:artifact.draftId,generation:artifact.generation} : {}),
            ...(artifact.versionId ? {versionId:artifact.versionId} : {}),
            ...(artifact.contentHash ? {contentHash:artifact.contentHash} : {})};
    });
    return {schemaVersion:1,executionId:value.executionId,state:value.state,stateRevision:value.stateRevision,
        createdAt:value.createdAt,updatedAt:value.updatedAt,operations,artifacts,
        ...(question ? {question} : {}),
        ...(replyAdmission ? {replyAdmission} : {}),
        ...(value.claimExpiresAt ? {claimExpiresAt:value.claimExpiresAt} : {}),
        ...(value.finalMessageId ? {finalMessageId:value.finalMessageId} : {})};
}

export class WorkspaceExecutions {
    constructor(workspace) { this.workspace=workspace; this.scopes=new Map(); }
    scope() { return JSON.stringify([this.workspace.workspaceId,this.workspace.authorityKey()]); }
    records() {
        const scope=this.scope();
        if (!this.scopes.has(scope)) {
            let stored;
            try { stored=JSON.parse(this.workspace.storage.getItem(`honua.workspace.executions:${scope}`) || '[]'); }
            catch { throw new Error('The original request reference could not be read. Reconnect before sending.'); }
            if (!Array.isArray(stored) || stored.length > 100
                || stored.some(row => !row || !identity(row.turnId) || typeof row.acknowledged !== 'boolean'
                    || row.requestDigest != null && !digest(row.requestDigest))
                || new Set(stored.map(row=>row.turnId)).size !== stored.length)
                throw new Error('The original request reference is invalid. Reconnect before sending.');
            // Stored IDs locate native observations; storage never supplies an execution state.
            this.scopes.set(scope,new Map(stored.map(row=>[row.turnId,{turnId:row.turnId,acknowledged:row.acknowledged,
                ...(row.requestDigest ? {requestDigest:row.requestDigest} : {})}])));
        }
        return this.scopes.get(scope);
    }
    persist(scope, records) {
        this.workspace.storage.setItem(`honua.workspace.executions:${scope}`,JSON.stringify(
            [...records.values()].map(row=>({turnId:row.turnId,acknowledged:row.acknowledged,
                ...(row.requestDigest ? {requestDigest:row.requestDigest} : {})}))));
    }
    requireResolvedSubmission() {
        if ([...this.records().values()].some(row=>!row.acknowledged))
            throw new Error('The previous message has not been confirmed. Check its original request before sending another.');
    }
    begin(turnId, requestDigest) {
        if (!identity(turnId)) throw new Error('The request identifier is invalid.');
        if (requestDigest != null && !digest(requestDigest)) throw new Error('The original message digest is invalid.');
        this.requireResolvedSubmission();
        const scope=this.scope(), records=this.records();
        if (records.has(turnId)) throw new Error('This request identifier already exists. Check the original request.');
        const previousRecords=new Map(records);
        if (records.size >= 100) {
            // Only current native observations can retire an entry. Persisted
            // acknowledgement alone cannot settle an execution after reload.
            const resolved=[...records.values()].find(row=>row.acknowledged && !row.error
                && (['completed','failed'].includes(row.execution?.state)
                    || Object.hasOwn(row,'execution') && row.execution === null));
            if (!resolved) throw new Error('Check your existing requests before starting more work.');
            records.delete(resolved.turnId);
        }
        const record={turnId,acknowledged:false,...(requestDigest ? {requestDigest} : {})};records.set(turnId,record);
        try { this.persist(scope,records); }
        catch (error) {
            records.clear();for (const [key,value] of previousRecords) records.set(key,value);
            throw error;
        }
        return {scope,workspaceId:this.workspace.workspaceId,session:{...this.workspace.session},records,record};
    }
    adopt(binding, turn) {
        if (!turn || turn.turnId !== binding.record.turnId
            || turn.context?.workspaceId && turn.context.workspaceId !== binding.workspaceId)
            throw new Error('The returned request does not match the original message.');
        const execution=turn.execution == null ? null : readExecution(turn.execution);
        const submission=readSubmission(turn.submission,binding.record,execution);
        const previous=binding.record.execution;
        if (binding.record.submission && JSON.stringify(binding.record.submission) !== JSON.stringify(submission))
            throw new Error('The original submission outcome changed. Check the request again.');
        if (submission?.disposition === 'not-dispatched' && (turn.status !== 'not-dispatched' || previous))
            throw new Error('The original accepted request cannot be replaced by a refusal.');
        if (previous && execution && previous.executionId !== execution.executionId)
            throw new Error('The execution identity changed. Check the original request.');
        if (previous && execution && execution.stateRevision < previous.stateRevision) return binding.record;
        if (previous?.question && JSON.stringify(previous.question) !== JSON.stringify(execution?.question))
            throw new Error('The original agent question changed. Check the original request.');
        if (previous && execution && execution.stateRevision === previous.stateRevision
            && JSON.stringify(execution) !== JSON.stringify(previous))
            throw new Error('Conflicting execution status was received. Check the original request.');
        // A legacy or incomplete observation cannot erase a known native execution.
        if (previous && !execution) throw new Error('Execution status is temporarily unavailable. Check again.');
        binding.record.acknowledged=true;
        binding.record.execution=execution;
        binding.record.submission=submission;
        binding.record.context=turn.context;
        binding.record.error=null;
        this.persist(binding.scope,binding.records);
        return binding.record;
    }
    async read(binding) {
        try {
            const turn=await this.workspace.api(`${binding.workspaceId}/turns/${encodeURIComponent(binding.record.turnId)}`,
                'GET',undefined,binding.session);
            if (this.scope() !== binding.scope) return null;
            this.adopt(binding,turn);
            return turn;
        } catch (error) {
            if (this.scope() === binding.scope) {
                binding.record.error=[401,403,404].includes(error.status)
                    ? 'This request cannot currently be read with your session. Reconnect to check the original request.'
                    : 'Progress could not be checked. Check again to read the original request.';
                // Revoked authority must not leave old private result links visible.
                if ([401,403,404].includes(error.status)) {
                    delete binding.record.execution;delete binding.record.context;
                }
            }
            throw error;
        }
    }
    async refresh(messages=[]) {
        const scope=this.scope(), records=this.records();
        for (const message of messages.slice(-100)) {
            if (message?.role !== 'user' || !identity(message.turnId) || records.has(message.turnId) || records.size >= 100) continue;
            records.set(message.turnId,{turnId:message.turnId,acknowledged:true});
        }
        this.persist(scope,records);
        const workspaceId=this.workspace.workspaceId, session={...this.workspace.session};
        // Read the latest conversation turns plus all unconfirmed/nonterminal requests.
        const recent=new Set([...records.keys()].slice(-10));
        const selected=[...records.values()].filter(row=>recent.has(row.turnId) || !row.acknowledged
            || row.execution && !['completed','failed'].includes(row.execution.state));
        for (const record of selected) {
            if (this.scope() !== scope) return;
            await this.read({scope,workspaceId,session,records,record}).catch(()=>{});
        }
    }
    current() { return [...this.records().values()]; }
    unavailable(error) {
        for (const record of this.records().values()) {
            record.error='Progress could not be checked. Reconnect to read the original request.';
            if ([401,403,404].includes(error.status)) {
                delete record.execution;delete record.context;
            }
        }
    }
    async original(turnId) {
        const record=this.records().get(turnId);
        if (!record) throw new Error('Reconnect to the original request before opening its result.');
        const binding={scope:this.scope(),workspaceId:this.workspace.workspaceId,
            session:{...this.workspace.session},records:this.records(),record};
        const turn=await this.read(binding);
        if (!turn) throw new Error('The workspace connection changed. Open the result again.');
        return record;
    }
}

export function executionSummary(record) {
    if (record.error) return record.error;
    if (!record.acknowledged) return 'Checking whether your original message was accepted.';
    if (record.submission?.disposition === 'not-dispatched') return refusalSummary(record.submission);
    if (!record.execution) return 'Message saved. Execution status is not available.';
    const state=record.execution.state;
    return state === 'awaiting-approval' ? 'Awaiting approval from another person.'
        : state === 'interrupted' ? 'Work was interrupted. Check the original request before retrying.'
        : labels[state];
}

export function renderExecutions(host, workspace, report) {
    if (!host) return;
    const records=workspace.executions.current();
    host.hidden=records.length === 0;
    const renderKey=JSON.stringify([workspace.executions.scope(),records.slice(-10)]);
    if (host.executionRenderKey === renderKey) return;
    host.executionRenderKey=renderKey;
    const active=host.contains(host.ownerDocument.activeElement)
        ? host.ownerDocument.activeElement?.dataset?.executionAction : null;
    const fragment=host.ownerDocument.createDocumentFragment();
    for (const record of records.slice(-10)) {
        const row=host.ownerDocument.createElement('section');
        row.setAttribute('aria-label','Agent request');
        const summary=host.ownerDocument.createElement('p');summary.textContent=executionSummary(record);row.append(summary);
        if (record.context) {
            const context=host.ownerDocument.createElement('p');context.className='console-muted';
            const resources=(record.context.selectedResources || []).map(resource=>resource.displayName || `${resource.kind} ${resource.id}`).join(', ');
            context.textContent=`Submitted context: ${resources || record.context.task?.kind || 'No resource selected'}`;
            row.append(context);
        }
        if (!record.error && record.execution) {
            if (record.execution.state === 'awaiting-input') {
                const question=host.ownerDocument.createElement('p');
                question.textContent=record.execution.question.text;row.append(question);
                const answer=host.ownerDocument.createElement('button');answer.type='button';answer.className='console-button';
                answer.textContent='Reply to question';answer.dataset.executionAction=`${record.turnId}:reply`;
                answer.addEventListener('click',()=>workspace.questionReplies.begin(record.turnId)
                    .then(()=>workspace.focusPrompt?.()).catch(report));row.append(answer);
            }
            for (const operation of record.execution.operations) {
                const detail=host.ownerDocument.createElement('p');
                const kind={'operation-proposal':'Proposed change','dataset-import':'Dataset import','managed-job':'Job'}[operation.kind];
                detail.textContent=`${kind}: ${operation.status}`;row.append(detail);
                if (operation.proposalId) {
                    const link=host.ownerDocument.createElement('a');
                    link.href=`/inbox?proposalId=${encodeURIComponent(operation.proposalId)}`;
                    link.dataset.executionAction=`${record.turnId}:proposal:${operation.proposalId}`;
                    link.textContent='Open approval';row.append(link);
                }
                if (operation.jobId) {
                    const link=host.ownerDocument.createElement('a');
                    link.href=`/operate/jobs/${encodeURIComponent(operation.jobId)}`;
                    link.dataset.executionAction=`${record.turnId}:job:${operation.jobId}`;
                    link.textContent='Open job';row.append(link);
                }
            }
            for (const artifact of record.execution.artifacts) {
                if (!artifact.draftId) continue;
                const button=host.ownerDocument.createElement('button');button.type='button';button.className='console-button';
                button.textContent=`Open ${artifact.family}`;
                button.dataset.executionAction=`${record.turnId}:draft:${artifact.draftId}`;
                button.addEventListener('click',()=>Promise.resolve().then(()=>workspace.openExecutionArtifact(record.turnId,artifact))
                    .catch(report));row.append(button);
            }
        }
        fragment.append(row);
    }
    host.replaceChildren(fragment);
    if (active) [...host.querySelectorAll('[data-execution-action]')]
        .find(control=>control.dataset.executionAction === active)?.focus();
}
