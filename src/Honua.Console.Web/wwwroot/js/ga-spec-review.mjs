// Events and model prose never update this record; all state is authorized read-back.
export class GaSpecReview {
    constructor(workspace) { this.workspace = workspace; this.plan = null; this.run = null; }
    get key() { return `honua.ga.review.${this.workspace.workspaceId}.${this.workspace.authorityKey()}`; }
    requireEnabled() { if (!this.workspace.session?.gaSpecWorkflow) throw new Error('GA spec workflow is disabled.'); }
    async planCurrentDraft() {
        this.requireEnabled();
        const workspace = this.workspace, key = this.key;
        if (!workspace.draft || workspace.dirty.size) throw new Error('Save the canonical layer draft before planning.');
        const context = await workspace.contextReference();
        const bundle = await workspace.api('ga/bundle');
        if (typeof bundle.bundleVersion !== 'string' || !bundle.grammar || !bundle.pathways)
            throw new Error('The server workflow bundle is unavailable.');
        const draft = await workspace.api(`${workspace.workspaceId}/drafts/${encodeURIComponent(workspace.draft.id)}`);
        if (key !== this.key || draft.revision !== workspace.draft.revision)
            throw new Error('The draft changed. Review it before planning.');
        const {connectionId,schema,table,layerName,serviceName} = draft.fields;
        const plan = await workspace.api('ga/plans', 'POST', {bundleVersion:bundle.bundleVersion,
            workspaceId:context.workspaceId,viewId:context.viewId,targetId:context.targetId,
            environment:workspace.session.environmentId,contextRevision:String(context.contextRevision),
            connectionId,schema,table,layerName,serviceName});
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
        if (existing?.commandId && (existing.planId !== planId || existing.revision !== revision)) {
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
        else if (saved.commandId) {
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
        if (saved.commandId) throw new Error('This Apply was already submitted. Read its recorded outcome before continuing.');
        const key = this.key;
        saved.commandId = this.workspace.uuid();
        this.workspace.storage.setItem(this.key, JSON.stringify(saved));
        const accepted = await this.workspace.api(`ga/plans/${saved.planId}/apply`, 'POST', {revision:saved.revision,commandId:saved.commandId});
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
    if (!plan) return 'Open a revision-pinned server plan to review a consequential change.';
    const fields = ['environment','workspaceId','viewId','targetId','connectionId','schema','table','layerName','serviceName'];
    const inputs = plan.spec || plan;
    return [`Pinned layer publication · ${plan.verdict}`, ...fields.filter(key => inputs[key] != null).map(key => `${key}: ${inputs[key]}`),
        `Context revision: ${inputs.contextRevision ?? 'unavailable'}`, `Plan revision: ${plan.revision}`,
        `Drafted by: ${plan.draftedBy ?? 'unavailable'}`, ...(plan.blockers || []),
        ...(run ? [`Execution: ${run.execution}`, `Evidence: ${run.evidence}`, `Applied by: ${run.appliedBy ?? 'unavailable'}`, ...(run.blockers || [])] : [])].join('\n');
}
