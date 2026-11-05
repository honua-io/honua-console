export class OperatorPreferences {
    constructor(storage) {
        this.storage = storage;
        this.key = null;
        this.authority = null;
        this.follow = true;
    }
    clear() {
        this.key = null;
        this.authority = null;
        this.follow = true;
    }
    bind(session) {
        this.clear();
        const opaqueId = value => typeof value === 'string' && /^[A-Za-z0-9_-]{1,256}$/.test(value);
        if (session?.authenticated !== true || !opaqueId(session.principalId) || !opaqueId(session.environmentId)
            || !(Date.parse(session.expiresAt) > Date.now())) return;
        this.key = `honua.shell.preferences.v1:${session.principalId}:${session.environmentId}`;
        this.authority = JSON.stringify([session.releaseId ?? null, session.sourcePathBase ?? null]);
        try {
            const record = JSON.parse(this.storage?.getItem(this.key) || 'null');
            if (record?.authority === this.authority && typeof record.follow === 'boolean') this.follow = record.follow;
            else this.storage?.removeItem(this.key);
        } catch { /* Storage is optional; a denied or invalid record keeps ephemeral defaults. */ }
    }
    save(values) {
        this.follow = values.follow === true;
        if (!this.key) return;
        try { this.storage?.setItem(this.key, JSON.stringify({authority: this.authority, follow: this.follow})); }
        catch { /* The preference remains usable when browser storage is unavailable. */ }
    }
}

export function mountWorkspacePreferences(workspace, host, signal) {
    let storage;
    try { storage = globalThis.localStorage; } catch { /* Private contexts may deny storage access. */ }
    const preferences = new OperatorPreferences(storage);
    const follow = host.querySelector('[data-workspace-follow]');
    let generation = 0, lastWorkspaceAuthority;
    function render() {
        workspace.follow = preferences.follow;
        if (follow) { follow.checked = preferences.follow; follow.disabled = !preferences.key; }
    }
    async function refresh() {
        const revision = ++generation;
        preferences.clear(); render();
        try {
            const session = await workspace.api('session');
            if (signal.aborted || revision !== generation) return;
            preferences.bind(session); render();
        } catch {
            if (!signal.aborted && revision === generation) { preferences.clear(); render(); }
        }
    }
    follow?.addEventListener('change', () => { preferences.save({follow: follow.checked}); render(); }, {signal});
    window.addEventListener('honua-console-navigation', refresh, {signal});
    return () => {
        const authority = workspace.authorityKey();
        if (authority === lastWorkspaceAuthority) return;
        lastWorkspaceAuthority = authority;
        void refresh();
    };
}
