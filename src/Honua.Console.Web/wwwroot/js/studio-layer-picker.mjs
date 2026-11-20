// Lists human-authorized published sources and keeps the selection bound to one
// workspace authority and draft. Studio revalidates the source before dispatch.
import { consoleHref } from './console-links.mjs';

export class StudioLayerPicker {
    constructor(app, isCurrentView) {
        this.app = app;
        this.isCurrentView = isCurrentView;
        this.abort = new AbortController();
        this.choices = [];
        this.binding = undefined;
        this.generation = 0;
    }
    capture() {
        const binding = this.app.workspaceCookieBinding;
        const draftId = this.app.workspaceDraftIdentity?.draftId;
        if (!this.isCurrentView() || !this.app.isConnected || this.abort.signal.aborted)
            throw new Error('The Studio view changed. Reopen the layer picker.');
        if (!draftId || !binding?.principalId || !binding.environmentId)
            throw new Error('Start or open a map before adding a layer.');
        return JSON.stringify([draftId, binding.principalId, binding.environmentId, binding.sourcePathBase ?? null]);
    }
    assertCurrent(binding) {
        if (this.capture() !== binding) throw new Error('The map or connection changed. Refresh the layer picker.');
    }
    async list() {
        const binding = this.capture();
        const generation = ++this.generation;
        this.binding = undefined;
        this.choices = [];
        let lease = this.app.session;
        let sources = await this.app.listRecipeSources(this.abort.signal);
        this.assertCurrent(binding);
        if (this.app.session !== lease) {
            // Do not display catalog data read with a replaced credential.
            lease = this.app.session;
            sources = await this.app.listRecipeSources(this.abort.signal);
            this.assertCurrent(binding);
            if (this.app.session !== lease) throw new Error('The connection changed. Refresh the layer picker.');
        }
        if (generation !== this.generation) throw new Error('The layer choices changed. Refresh the picker.');
        const choices = sources.flatMap(source => {
            if (source?.protocol !== 'geoservices-feature-service' || typeof source.id !== 'string'
                || typeof source.title !== 'string' || !Number.isSafeInteger(source.locator?.layerId)
                || source.locator.layerId < 0 || typeof source.locator.url !== 'string') return [];
            try {
                const url = new URL(source.locator.url, globalThis.location?.origin ?? 'http://localhost');
                if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) return [];
                const match = /\/rest\/services\/(.+)\/FeatureServer\/?$/.exec(url.pathname);
                if (!match) return [];
                const segments = match[1].split('/').map(decodeURIComponent);
                if (segments.some(value => !value || value === '.' || value === '..' || /[\/\\\x00-\x1f]/.test(value))) return [];
                return [{ id: source.id, title: source.title, serviceName: segments.join('/'), layerId: source.locator.layerId }];
            } catch { return []; }
        });
        if (new Set(choices.map(choice => choice.id)).size !== choices.length)
            throw new Error('The authorized catalog has ambiguous layer identities. Refresh and try again.');
        this.binding = binding;
        this.choices = choices;
        return choices.map(choice => ({ ...choice }));
    }
    async add(id) {
        if (!this.binding) throw new Error('Refresh the layer choices first.');
        const binding = this.binding;
        const generation = this.generation;
        this.assertCurrent(binding);
        const selected = this.choices.find(choice => choice.id === id);
        if (!selected) throw new Error('Choose a layer from the current authorized catalog.');
        // Studio refuses every edit while one waits; only a hold this call creates belongs to this layer.
        const prior = this.app.pendingEditorApproval?.proposalId;
        if (prior) throw new HeldEditRefusal(prior);
        const result = await this.app.addPublishedLayer({ serviceName: selected.serviceName,
            layerId: selected.layerId, layerName: selected.title }, { isCurrent: () => {
                try { return this.binding === binding && this.generation === generation && this.capture() === binding; }
                catch { return false; }
            } });
        this.assertCurrent(binding);
        if (this.generation !== generation || this.binding !== binding)
            throw new Error('The layer choices changed. Refresh the picker.');
        return readAddOutcome(result);
    }
    // Studio owns the hold; this view shows it only for its own current map and authority.
    heldEdit() {
        const proposalId = this.app.pendingEditorApproval?.proposalId;
        if (typeof proposalId !== 'string' || !proposalId) return undefined;
        try { const binding = this.capture(); return { proposalId, draftId: JSON.parse(binding)[0], binding }; }
        catch { return undefined; }
    }
    // A reload ends Studio's hold once the draft moved past the held write; it reports whether the hold ended.
    // No editor turn is prepared first: Studio refuses one while the hold lasts.
    async reloadHeld(held) {
        this.assertCurrent(held.binding);
        await this.app.attachWorkspaceDraft(held.draftId);
        return this.app.pendingEditorApproval?.proposalId !== held.proposalId;
    }
    async discardHeld(held) {
        this.assertCurrent(held.binding);
        this.app.discardWorkspaceApprovalHold();
        await this.app.attachWorkspaceDraft(held.draftId);
    }
    dispose() { this.generation++; this.abort.abort(); this.binding = undefined; this.choices = []; }
}

// Studio refuses another edit while one waits; the refusal applies only while that hold lasts.
class HeldEditRefusal extends Error {
    constructor(proposalId) {
        super(`Proposal ${proposalId} is waiting for separate human approval. Check the approval or stop waiting before adding another layer.`);
    }
}

// A held edit is not a failure: it carries the proposal a person approves in the Inbox.
function readAddOutcome(result) {
    const proposalId = result?.approval?.proposalId;
    if (typeof proposalId === 'string' && proposalId) return { held: true };
    if (!result?.ok) throw new Error(result?.reason || 'The layer could not be added to the map.');
    return { held: false };
}

function heldEditControls(section) {
    const notice = document.createElement('p'); notice.setAttribute('role', 'status'); notice.tabIndex = -1;
    const inbox = document.createElement('a'); inbox.textContent = 'Review approval in Inbox';
    const reload = document.createElement('button'); reload.type = 'button'; reload.textContent = 'Check approval and reload map';
    const discard = document.createElement('button'); discard.type = 'button'; discard.textContent = 'Stop waiting and reload map';
    inbox.className = reload.className = discard.className = 'console-button console-button-secondary';
    section.append(notice, inbox, reload, discard);
    return { notice, inbox, reload, discard };
}

// Mirrors Studio's hold, including one started elsewhere or still active when this view mounts.
// onRelease runs when a hold ends, so text that only applied while it lasted can be cleared.
export function mountHeldEdit({ section, picker, app, onRelease }) {
    const { notice, inbox, reload, discard } = heldEditControls(section);
    let held, busy = false;
    const render = () => {
        const wasHeld = Boolean(held);
        held = picker.heldEdit();
        inbox.hidden = reload.hidden = discard.hidden = !held;
        reload.disabled = discard.disabled = busy;
        if (!held) { if (wasHeld) { notice.textContent = ''; onRelease?.(); } return; }
        notice.textContent = `A map edit is waiting for separate human approval. Proposal ${held.proposalId}. Approve it in the Inbox, then check the approval here.`;
        inbox.href = consoleHref(`/inbox?proposalId=${encodeURIComponent(held.proposalId)}`, inbox.ownerDocument?.baseURI);
    };
    const run = async action => {
        if (!held || busy) return;
        const current = held; busy = true; render();
        try { notice.textContent = await action(current); }
        catch (error) { notice.textContent = error instanceof Error ? error.message : 'The map could not be reloaded.'; }
        finally { busy = false; const text = notice.textContent; render(); notice.textContent = text; if (!held) notice.focus?.(); }
    };
    reload.addEventListener('click', () => run(async current => (await picker.reloadHeld(current))
        ? 'Studio ended the hold. The map shows the server draft.'
        : `Still waiting for separate human approval. Proposal ${current.proposalId}.`), { signal: picker.abort.signal });
    discard.addEventListener('click', () => run(async current => {
        await picker.discardHeld(current);
        return `Stopped waiting for proposal ${current.proposalId}. The map shows the server draft.`;
    }), { signal: picker.abort.signal });
    for (const name of ['honua-studio-editor-approval-change', 'honua-workspace-draft-change'])
        app.addEventListener?.(name, () => { if (!busy) render(); }, { signal: picker.abort.signal });
    render();
    return { render };
}

// A message showing a hold's refusal; a later message from another action is not one.
export const showsHoldRefusal = message => 'holdRefusal' in message.dataset;

async function addChoice({ picker, choice, button, open, message, list, status, heldEdit }) {
    button.disabled = true; open.disabled = true; message.textContent = 'Adding layer…'; delete message.dataset.holdRefusal;
    try {
        const { held } = await picker.add(choice.id);
        message.textContent = held ? '' : `${choice.title} added to the map.`;
        delete message.dataset.holdRefusal;
        if (held) heldEdit.render();
        status.textContent = message.textContent;
        list.hidden = true; open.setAttribute('aria-expanded', 'false');
    } catch (error) {
        message.textContent = error instanceof Error ? error.message : 'The layer could not be added.';
        if (error instanceof HeldEditRefusal) message.dataset.holdRefusal = '';
    }
    finally { button.disabled = false; open.disabled = false; }
}

export function mountStudioLayerPicker({ app, host, status }) {
    const picker = new StudioLayerPicker(app, () => host.isConnected);
    const section = document.createElement('section');
    section.setAttribute('aria-label', 'Add published layers');
    const open = document.createElement('button');
    open.type = 'button'; open.textContent = 'Choose published layer'; open.setAttribute('aria-expanded', 'false');
    open.className = 'console-button console-button-secondary';
    const message = document.createElement('p'); message.setAttribute('role', 'status');
    const list = document.createElement('ul'); list.hidden = true;
    section.append(open, message, list);
    const heldEdit = mountHeldEdit({ section, picker, app, onRelease: () => {
        if (!showsHoldRefusal(message)) return;
        delete message.dataset.holdRefusal; message.textContent = '';
    } });
    host.querySelector('[data-studio-mount]').before(section);
    open.addEventListener('click', async () => {
        if (!list.hidden) { list.hidden = true; open.setAttribute('aria-expanded', 'false'); return; }
        open.disabled = true; message.textContent = 'Loading published layers…'; delete message.dataset.holdRefusal; list.replaceChildren();
        try {
            const choices = await picker.list();
            for (const choice of choices) {
                const row = document.createElement('li'); const button = document.createElement('button');
                button.type = 'button'; button.textContent = `Add ${choice.title} to map`;
                button.className = 'console-button console-button-secondary console-button-small';
                button.addEventListener('click', () => addChoice({ picker, choice, button, open, message, list, status, heldEdit }),
                    { signal: picker.abort.signal });
                row.append(button); list.append(row);
            }
            message.textContent = choices.length ? 'Choose a published layer.' : 'No published vector layers are available.';
            list.hidden = false; open.setAttribute('aria-expanded', 'true');
        } catch (error) { message.textContent = error instanceof Error ? error.message : 'The catalog could not be loaded.'; }
        finally { open.disabled = false; }
    }, { signal: picker.abort.signal });
    return { dispose() { picker.dispose(); section.remove(); } };
}
