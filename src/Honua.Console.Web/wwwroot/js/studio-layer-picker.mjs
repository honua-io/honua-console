// Lists human-authorized published sources and keeps the selection bound to one
// workspace authority and draft. Studio revalidates the source before dispatch.
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
        const result = await this.app.addPublishedLayer({ serviceName: selected.serviceName,
            layerId: selected.layerId, layerName: selected.title }, { isCurrent: () => {
                try { return this.binding === binding && this.generation === generation && this.capture() === binding; }
                catch { return false; }
            } });
        this.assertCurrent(binding);
        if (this.generation !== generation || this.binding !== binding)
            throw new Error('The layer choices changed. Refresh the picker.');
        if (!result?.ok) throw new Error(result?.reason || 'The layer could not be added to the map.');
    }
    dispose() { this.generation++; this.abort.abort(); this.binding = undefined; this.choices = []; }
}

export function mountStudioLayerPicker({ app, host, status }) {
    const picker = new StudioLayerPicker(app, () => host.isConnected);
    const section = document.createElement('section');
    section.setAttribute('aria-label', 'Add published layers');
    const open = document.createElement('button');
    open.type = 'button'; open.textContent = 'Choose published layer'; open.setAttribute('aria-expanded', 'false');
    const message = document.createElement('p'); message.setAttribute('role', 'status');
    const list = document.createElement('ul'); list.hidden = true;
    section.append(open, message, list);
    host.querySelector('[data-studio-mount]').before(section);
    open.addEventListener('click', async () => {
        if (!list.hidden) { list.hidden = true; open.setAttribute('aria-expanded', 'false'); return; }
        open.disabled = true; message.textContent = 'Loading published layers…'; list.replaceChildren();
        try {
            const choices = await picker.list();
            for (const choice of choices) {
                const row = document.createElement('li'); const button = document.createElement('button');
                button.type = 'button'; button.textContent = `Add ${choice.title} to map`;
                button.addEventListener('click', async () => {
                    button.disabled = true; open.disabled = true; message.textContent = 'Adding layer…';
                    try {
                        await picker.add(choice.id);
                        message.textContent = `${choice.title} added to the map.`;
                        status.textContent = message.textContent;
                        list.hidden = true; open.setAttribute('aria-expanded', 'false');
                    } catch (error) { message.textContent = error instanceof Error ? error.message : 'The layer could not be added.'; }
                    finally { button.disabled = false; open.disabled = false; }
                }, { signal: picker.abort.signal });
                row.append(button); list.append(row);
            }
            message.textContent = choices.length ? 'Choose a published layer.' : 'No published vector layers are available.';
            list.hidden = false; open.setAttribute('aria-expanded', 'true');
        } catch (error) { message.textContent = error instanceof Error ? error.message : 'The catalog could not be loaded.'; }
        finally { open.disabled = false; }
    }, { signal: picker.abort.signal });
    return { dispose() { picker.dispose(); section.remove(); } };
}
