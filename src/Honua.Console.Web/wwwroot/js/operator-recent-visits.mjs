import {browserStorage} from './operator-preferences.mjs';

const maxVisits = 6;
const maxPathLength = 1024;
const maxLabelLength = 160;
const maxStoredLength = 16384;

function supportedPath(path) {
    return typeof path === 'string' && path.length <= maxPathLength
        && /^\/(?:operate(?:\/[A-Za-z0-9_%.-]+)*|studio|studio\/processes|catalog|inbox|environments)$/.test(path)
        && new URL(path, 'https://console.invalid').pathname === path;
}

function validVisit(visit) {
    return supportedPath(visit?.path) && typeof visit.label === 'string'
        && visit.label.trim().length > 0 && visit.label.length <= maxLabelLength;
}

export class OperatorRecentVisits {
    constructor(storage) { this.storage = storage; this.clear(); }
    clear() { this.key = null; this.authority = null; this.expiresAt = 0; this.items = []; }
    bind(partition) {
        if (!partition || partition.expiresAt <= Date.now()) { this.clear(); return; }
        const key = `${partition.key}:recent.v1`;
        if (key === this.key && partition.authority === this.authority) {
            this.expiresAt = partition.expiresAt; return;
        }
        this.clear();
        this.key = key; this.authority = partition.authority; this.expiresAt = partition.expiresAt;
        this.restore();
    }
    restore() {
        try {
            const stored = this.storage?.getItem(this.key);
            if (!stored || stored.length > maxStoredLength) return;
            const record = JSON.parse(stored);
            if (record?.authority !== this.authority || !Array.isArray(record.items)) {
                this.storage?.removeItem(this.key); return;
            }
            this.items = record.items.filter(validVisit).slice(0, maxVisits).map(({path, label}) => ({path, label}));
        } catch { /* Storage is optional; a denied or invalid record leaves an empty history. */ }
    }
    record(visit) {
        if (this.expiresAt <= Date.now()) this.clear();
        if (!this.key || !validVisit(visit)) return;
        this.items = [visit, ...this.items.filter(item => item.path !== visit.path)].slice(0, maxVisits);
        try { this.storage?.setItem(this.key, JSON.stringify({authority: this.authority, items: this.items})); }
        catch { /* Recent visits remain usable in memory when storage is unavailable. */ }
    }
}

class RecentVisitsView {
    constructor(history) { this.history = history; this.validated = false; }
    render() {
        const host = document.querySelector('[data-home-recent]');
        this.host = host;
        if (!host) return;
        const list = host.querySelector('[data-recent-items]');
        const items = this.validated ? this.history.items : [];
        list.replaceChildren(...items.map(item => {
            const row = document.createElement('li');
            const link = document.createElement('a');
            link.href = item.path; link.textContent = item.label;
            row.append(link); return row;
        }));
        list.hidden = items.length === 0;
        host.querySelector('[data-recent-status]').hidden = items.length > 0;
    }
    recordCurrentPage() {
        const marker = document.querySelector('[data-console-location]');
        const heading = document.querySelector('#console-main h1');
        const blocked = heading?.closest('.console-state:not(.console-state-empty), .operate-status-denied, .operate-status-loading');
        if (this.validated && heading && !blocked)
            this.history.record({path: marker?.dataset.consoleLocation, label: heading.textContent.trim().slice(0, maxLabelLength)});
        this.render();
    }
}

export function mountRecentVisits(signal) {
    const history = new OperatorRecentVisits(browserStorage());
    const view = new RecentVisitsView(history);
    let expiry;
    const observer = new MutationObserver(() => {
        if (document.querySelector('[data-home-recent]') !== view.host) view.render();
    });
    observer.observe(document.body, {childList: true, subtree: true});
    signal.addEventListener('abort', () => { observer.disconnect(); clearTimeout(expiry); }, {once: true});
    return partition => {
        if (signal.aborted) return;
        clearTimeout(expiry);
        view.validated = Boolean(partition && partition.expiresAt > Date.now());
        if (view.validated) history.bind(partition);
        view.recordCurrentPage();
        if (view.validated) expiry = setTimeout(() => { view.validated = false; view.render(); }, history.expiresAt - Date.now());
    };
}
