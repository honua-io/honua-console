import {hasOperatorShell} from './console-shell-context.mjs';

function validatedPartition(session) {
    const opaqueId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,256}$/.test(value);
    const expiresAt = Date.parse(session?.expiresAt);
    if (session?.authenticated !== true || !opaqueId(session.principalId) || !opaqueId(session.environmentId)
        || !(expiresAt > Date.now())) return null;
    return {key: `honua.shell.preferences.v1:${session.principalId}:${session.environmentId}`,
        authority: JSON.stringify([session.releaseId ?? null, session.sourcePathBase ?? null]), expiresAt};
}

export class OperatorPreferences {
    constructor(storage) { this.storage = storage; this.clear(); }
    clear() {
        this.key = null;
        this.authority = null;
        this.expiresAt = 0;
        this.follow = true;
    }
    bind(session) { this.adopt(validatedPartition(session)); }
    adopt(partition) {
        if (!partition) { this.clear(); return; }
        if (!this.matchesPartition(partition)) this.clear();
        Object.assign(this, partition);
        this.restore();
    }
    matchesPartition(partition) {
        return this.key === partition.key && this.authority === partition.authority;
    }
    restore() {
        try {
            const record = JSON.parse(this.storage?.getItem(this.key) || 'null');
            if (record?.authority === this.authority && typeof record.follow === 'boolean') this.follow = record.follow;
            else this.storage?.removeItem(this.key);
        } catch { /* Storage is optional; a denied or invalid record preserves the in-memory choice. */ }
    }
    save(values) {
        if (!(this.expiresAt > Date.now())) this.clear();
        this.follow = values.follow === true;
        if (!this.key) return;
        try { this.storage?.setItem(this.key, JSON.stringify({authority: this.authority, follow: this.follow})); }
        catch { /* The preference remains usable when browser storage is unavailable. */ }
    }
}

export function browserStorage() {
    try { return globalThis.localStorage; } catch { /* Private contexts may deny storage access. */ }
}

class WorkspacePreferenceAdapter {
    constructor(workspace, host, signal) {
        this.workspace = workspace;
        this.signal = signal;
        this.follow = host.querySelector('[data-workspace-follow]');
        this.preferences = new OperatorPreferences(browserStorage());
        this.generation = 0;
        this.validated = false;
        this.follow?.addEventListener('change', () => {
            if (!this.validated) return;
            this.preferences.save({follow: this.follow.checked}); this.render();
        }, {signal});
        for (const event of ['honua-console-navigation', 'honua-console-authority'])
            window.addEventListener(event, () => { void this.refresh(); }, {signal});
    }
    render() {
        this.workspace.follow = this.preferences.follow;
        if (this.follow) {
            this.follow.checked = this.preferences.follow;
            this.follow.disabled = !this.validated || !this.preferences.key;
        }
    }
    unavailable(revision) {
        if (this.signal.aborted || revision !== this.generation) return;
        this.validated = false;
        if (this.lastValidatedWorkspaceAuthority !== this.workspace.authorityKey()) this.preferences.clear();
        this.render();
    }
    adoptCurrentPartition(partition) {
        this.preferences.adopt(partition);
        this.lastValidatedWorkspaceAuthority = this.workspace.authorityKey();
        this.validated = true;
        this.render();
    }
    async refresh() {
        const revision = ++this.generation;
        if (!hasOperatorShell() || !validatedPartition(this.workspace.session)) { this.unavailable(revision); return; }
        const workspaceAuthority = this.workspace.authorityKey();
        this.validated = false;
        if (this.follow) this.follow.disabled = true;
        try {
            const session = await this.workspace.api('session');
            if (this.signal.aborted || revision !== this.generation || workspaceAuthority !== this.workspace.authorityKey()) return;
            const partition = validatedPartition(session);
            if (!partition) { this.unavailable(revision); return; }
            this.adoptCurrentPartition(partition);
        } catch { this.unavailable(revision); }
    }
    sync() {
        const lease = JSON.stringify([this.workspace.authorityKey(), this.workspace.session?.expiresAt]);
        if (lease === this.lastWorkspaceLease) return;
        this.lastWorkspaceLease = lease;
        this.generation++;
        const partition = validatedPartition(this.workspace.session);
        if (!partition) { this.unavailable(this.generation); return; }
        const unchangedWorkspace = this.lastValidatedWorkspaceAuthority === this.workspace.authorityKey();
        if (unchangedWorkspace && !this.preferences.matchesPartition(partition)) { void this.refresh(); return; }
        this.adoptCurrentPartition(partition);
    }
}

export function mountWorkspacePreferences(workspace, host, signal) {
    const adapter = new WorkspacePreferenceAdapter(workspace, host, signal);
    return () => adapter.sync();
}
