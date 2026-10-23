// Events and model prose never update this record; all state is authorized read-back.
export class GaSpecReview {
    constructor(workspace) { this.workspace = workspace; this.plan = null; this.run = null; }
    get key() { return `honua.ga.review.${this.workspace.workspaceId}.${this.workspace.authorityKey()}`; }
    requireEnabled() { if (!this.workspace.session?.gaSpecWorkflow) throw new Error('GA spec workflow is disabled.'); }
    async planCurrentDraft() {
        this.requireEnabled();
        const workspace = this.workspace, key = this.key;
        if (!workspace.draft || workspace.dirty.size) throw new Error('Save the canonical layer draft before planning.');
        const draftId = workspace.draft.id, draftRevision = workspace.draft.revision;
        const workspaceId = workspace.workspaceId, viewId = workspace.viewId, generation = workspace.contextGeneration;
        const contextRevision = workspace.context?.revision, targetId = workspace.context?.targetId;
        const binding = {...workspace.session};
        const check = () => {
            if (key !== this.key || binding.environmentId !== workspace.session?.environmentId
                || binding.principalId !== workspace.session?.principalId || workspaceId !== workspace.workspaceId || viewId !== workspace.viewId
                || generation !== workspace.contextGeneration || draftId !== workspace.draft?.id
                || draftRevision !== workspace.draft?.revision || workspace.dirty.size
                || contextRevision !== workspace.context?.revision || targetId !== workspace.context?.targetId)
                throw new Error('The draft or workspace context changed. Review it before planning.');
        };
        const context = await workspace.contextReference();
        check();
        if (context.workspaceId !== workspaceId || context.viewId !== viewId
            || context.contextRevision !== contextRevision || context.targetId !== targetId)
            throw new Error('The workspace context changed. Review it before planning.');
        const bundle = await workspace.api('ga/bundle', 'GET', undefined, binding);
        check();
        if (typeof bundle.bundleVersion !== 'string' || !bundle.grammar || !bundle.pathways)
            throw new Error('The server workflow bundle is unavailable.');
        const draft = await workspace.api(`${workspaceId}/drafts/${encodeURIComponent(draftId)}`, 'GET', undefined, binding);
        check();
        if (draft.revision !== draftRevision)
            throw new Error('The draft changed. Review it before planning.');
        const {connectionId,schema,table,layerName,serviceName} = draft.fields;
        const plan = await workspace.api('ga/plans', 'POST', {bundleVersion:bundle.bundleVersion,
            workspaceId:context.workspaceId,viewId:context.viewId,targetId:context.targetId,
            environment:binding.environmentId,contextRevision:String(context.contextRevision),
            connectionId,schema,table,layerName,serviceName}, binding);
        check();
        return this.open(plan.planId, plan.revision);
    }
    async open(planId, revision) {
        this.requireEnabled();
        if (!/^[A-Za-z0-9_-]{1,128}$/.test(planId) || typeof revision !== 'string' || !revision || revision.length > 256)
            throw new Error('A revision-pinned plan reference is required.');
        const key = this.key;
        const plan = await this.workspace.api(`ga/plans/${planId}?revision=${encodeURIComponent(revision)}`);
        if (key !== this.key) throw new Error('Workspace authority changed. Reopen review.');
        if (plan.planId !== planId || plan.revision !== revision) throw new Error('The plan reference changed. Request a new review.');
        const existing = JSON.parse(this.workspace.storage.getItem(key) || 'null');
        if (existing?.commandId && existing.dispatch !== 'not-dispatched' && (existing.planId !== planId || existing.revision !== revision)) {
            // Re-read the original durable run before releasing this recovery slot.
            // Assistant text, Apply responses and stored client markers cannot release it.
            const observed = await this.workspace.api(`ga/runs/${existing.runId || existing.planId}`);
            if (key !== this.key) throw new Error('Workspace authority changed. Reopen review.');
            if (observed.runId !== (existing.runId || existing.planId)
                || !['succeeded','failed','cancelled'].includes(observed.execution))
                throw new Error('Resolve the original Apply before opening another plan.');
            this.workspace.storage.setItem(`${key}.history.${existing.commandId}`, JSON.stringify({...existing, outcome:observed}));
        }
        this.plan = plan; this.run = null;
        this.workspace.storage.setItem(key, JSON.stringify(existing?.planId === planId && existing?.revision === revision ? existing : {planId, revision}));
        return plan;
    }
    async reconnect() {
        this.requireEnabled();
        const key = this.key;
        const saved = JSON.parse(this.workspace.storage.getItem(key) || 'null');
        if (!saved) return;
        await this.open(saved.planId, saved.revision);
        this.workspace.storage.setItem(this.key, JSON.stringify(saved));
        if (saved.runId) {
            const observed = await this.workspace.api(`ga/runs/${saved.runId}`);
            if (key !== this.key) throw new Error('Workspace authority changed. Reconnect the original workspace.');
            this.run = observed;
        }
        else if (saved.commandId && saved.dispatch !== 'not-dispatched') {
            try {
                const observed = await this.workspace.api(`ga/runs/${saved.planId}`);
                if (key !== this.key) throw new Error('Workspace authority changed. Reconnect the original workspace.');
                saved.runId = saved.planId;
                this.workspace.storage.setItem(key, JSON.stringify(saved));
                this.run = observed;
            } catch (error) {
                throw new Error('Apply outcome is unknown. Keep the original command identity; read durable status again before another Apply.', {cause:error});
            }
        }
        return this.run || this.plan;
    }
    async apply() {
        this.requireEnabled();
        if (!this.plan || !['create','noop'].includes(this.plan.verdict) || this.plan.execution !== 'planned') throw new Error('Open an executable plan first.');
        if (this.plan.spec?.contextRevision != null && String(this.workspace.context?.revision) !== String(this.plan.spec.contextRevision))
            throw new Error('The workspace context changed. Request a new plan before Apply.');
        const saved = JSON.parse(this.workspace.storage.getItem(this.key) || 'null');
        if (!saved || saved.planId !== this.plan.planId || saved.revision !== this.plan.revision) throw new Error('Review changed. Open it again.');
        if (saved.commandId && saved.dispatch !== 'not-dispatched') throw new Error('This Apply was already submitted. Read its recorded outcome before continuing.');
        const key = this.key;
        saved.commandId ||= this.workspace.uuid();
        delete saved.dispatch;
        this.workspace.storage.setItem(key, JSON.stringify(saved));
        let accepted;
        try {
            accepted = await this.workspace.api(`ga/plans/${saved.planId}/apply`, 'POST', {revision:saved.revision,commandId:saved.commandId});
        } catch (error) {
            // Only the same-origin BFF can attest that the command never reached upstream.
            // Ambiguous failures retain the original recovery marker and cannot be replayed.
            const current = JSON.parse(this.workspace.storage.getItem(key) || 'null');
            if (error.notDispatched === true && current?.commandId === saved.commandId
                && current.planId === saved.planId && current.revision === saved.revision) {
                saved.dispatch = 'not-dispatched';
                this.workspace.storage.setItem(key, JSON.stringify(saved));
            }
            throw error;
        }
        if (!/^[A-Za-z0-9_-]{1,128}$/.test(accepted.runId)) throw new Error('Apply outcome is unknown. Reconcile the original command.');
        if (key !== this.key) throw new Error('Workspace authority changed. Reconnect the original workspace to read the outcome.');
        saved.runId = accepted.runId;
        this.workspace.storage.setItem(this.key, JSON.stringify(saved));
        // Even an Apply response is followed by the authorized durable status read.
        const observed = await this.workspace.api(`ga/runs/${saved.runId}`);
        if (key !== this.key) throw new Error('Workspace authority changed. Reconnect the original workspace.');
        this.run = observed;
        return this.run;
    }
}
export function reviewSummary(plan, run) {
    if (!plan) return 'Review your saved layer draft, or open a review from another agent.';
    const inputs = plan.spec || plan;
    const action = {create:'Create layer',noop:'Keep existing layer',refuse:'Publication blocked'}[plan.verdict] || 'Review required';
    return [`${action}: ${inputs.layerName || 'Unnamed layer'}`,
        `Service: ${inputs.serviceName || 'Unavailable'}`,
        `Source table: ${[inputs.schema,inputs.table].filter(Boolean).join('.') || 'Unavailable'}`,
        `Pinned environment: ${inputs.environmentName || inputs.environment || 'Unavailable'}`,
        `Pinned target: ${inputs.targetName || inputs.targetId || 'Unavailable'}`,
        `Execution: ${run?.execution || plan.execution || 'unknown'}`,
        `Evidence: ${run?.evidence || plan.evidence || 'unverified'}`,
        ...(plan.blockers || []), ...(run?.blockers || [])].join('\n');
}
export function reviewDetails(plan, run) {
    if (!plan) return 'No plan selected.';
    const inputs = plan.spec || plan;
    const fields = {environment:'Environment ID',workspaceId:'Workspace ID',viewId:'View ID',targetId:'Target ID',connectionId:'Connection ID',contextRevision:'Context revision',bundleVersion:'Workflow bundle'};
    return [...Object.entries(fields).filter(([key]) => inputs[key] != null).map(([key,label]) => `${label}: ${inputs[key]}`),
        `Plan ID: ${plan.planId}`, `Plan revision: ${plan.revision}`,
        `Drafted by: ${plan.draftedBy ?? 'unavailable'}`,
        ...(run ? [`Run ID: ${run.runId}`, `Applied by: ${run.appliedBy ?? 'unavailable'}`] : [])].join('\n');
}
