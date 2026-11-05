import {browserStorage} from './operator-preferences.mjs';

const maxVisits = 6;
const maxPathLength = 1024;
const maxLabelLength = 160;
const maxStoredLength = 16384;

const routeFamilies = [
    /^\/operate(?:\/[A-Za-z0-9_%.-]+)*$/,
    /^\/studio(?:\/(?:proof|drafts|processes|form|report))?$/,
    /^\/studio\/(?:styles|automations)(?:\/[A-Za-z0-9_%.-]+)?$/,
    /^\/studio\/workflows\/[A-Za-z0-9_%.-]+$/,
    /^\/studio\/apps\/[A-Za-z0-9_%.-]+\/preview$/,
    /^\/(?:catalog|environments)(?:\/[A-Za-z0-9_%.-]+)?$/,
    /^\/share\/public(?:\/items\/[A-Za-z0-9_%.-]+)?$/,
    /^\/maps\/[A-Za-z0-9_%.-]+$/,
    /^\/(?:inbox|approvals)$/
];

function safeSegment(segment) {
    const decoded = decodeURIComponent(segment);
    return decoded !== '.' && decoded !== '..' && !/[\\/?#%\u0000-\u001f\u007f]/.test(decoded);
}

function supportedPath(path) {
    if (typeof path !== 'string' || path.length > maxPathLength || !routeFamilies.some(family => family.test(path))) return false;
    try { return path.split('/').every(safeSegment) && new URL(path, 'https://console.invalid').pathname === path; }
    catch { return false; }
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
        const key = partition.key;
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
        this.items = [{path: visit.path, label: visit.label}, ...this.items.filter(item => item.path !== visit.path)].slice(0, maxVisits);
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
        const status = host.querySelector('[data-recent-status]');
        status.hidden = items.length > 0;
        status.textContent = this.validated ? 'No recent visits in this environment yet.'
            : 'Recent visits are paused until the current operator and environment are verified.';
    }
    recordCurrentPage(marker) {
        const heading = document.querySelector('#console-main h1');
        const blocked = heading?.closest('.console-state:not(.console-state-empty), .operate-status-denied, .operate-status-loading');
        if (this.validated && heading && !blocked)
            this.history.record({path: marker?.dataset.consoleLocation, label: (marker.dataset.consoleRecentLabel || heading.textContent).trim().slice(0, maxLabelLength)});
        this.render();
    }
}

function recentPartition(marker) {
    const scope = marker?.dataset.consoleRecentScope;
    const expiresAt = Number(marker?.dataset.consoleRecentUntil);
    if (!/^[a-f0-9]{64}$/.test(scope || '') || !Number.isFinite(expiresAt) || !(expiresAt > Date.now())) return null;
    return {key: `honua.shell.recent.v1:${scope}`, authority: scope, expiresAt};
}

export function mountRecentVisits(signal) {
    const history = new OperatorRecentVisits(browserStorage());
    const view = new RecentVisitsView(history);
    let expiry, lastSnapshot;
    function observe() {
        if (signal.aborted) return;
        const marker = document.querySelector('[data-console-location]');
        const snapshot = JSON.stringify([marker?.outerHTML, document.querySelector('#console-main h1')?.outerHTML]);
        if (snapshot === lastSnapshot) {
            if (document.querySelector('[data-home-recent]') !== view.host) view.render();
            return;
        }
        lastSnapshot = snapshot; clearTimeout(expiry);
        const partition = recentPartition(marker);
        view.validated = Boolean(partition);
        if (partition) history.bind(partition);
        view.recordCurrentPage(marker);
        if (partition) expiry = setTimeout(() => { view.validated = false; view.render(); }, partition.expiresAt - Date.now());
    }
    const observer = new MutationObserver(observe);
    observer.observe(document.body, {childList: true, characterData: true, subtree: true, attributes: true,
        attributeFilter: ['data-console-location', 'data-console-recent-scope', 'data-console-recent-until', 'data-console-recent-label']});
    signal.addEventListener('abort', () => { observer.disconnect(); clearTimeout(expiry); }, {once: true});
    observe();
}
