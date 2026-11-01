/** Browse server-owned Studio content and attach a selected map under the current session. */
export function mountStudioSavedMaps({ app, host, status }) {
    const section = document.createElement('section');
    section.setAttribute('aria-label', 'Saved maps');
    const toggle = document.createElement('button');
    toggle.type = 'button'; toggle.textContent = 'Open saved maps';
    toggle.className = 'console-button console-button-secondary';
    section.append(toggle);
    const mount = host.querySelector('[data-studio-mount]');
    if (mount) mount.before(section); else host.append(section);
    let browser, panel, binding, auth, busy = false, disposed = false, generation = 0;
    const authority = () => JSON.stringify(app.workspaceCookieBinding);
    const current = token => token === generation && !disposed && host.isConnected && app.isConnected && authority() === binding && app.auth === auth;
    const close = () => {
        generation++;
        browser?.remove(); panel?.remove(); browser = panel = undefined;
        toggle.textContent = 'Open saved maps'; toggle.setAttribute('aria-expanded', 'false');
    };
    const attach = async (draftId, token) => {
        if (!current(token) || busy || typeof draftId !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(draftId)) return;
        const previous = app.workspaceDraftIdentity?.draftId;
        busy = true;
        try {
            await app.prepareWorkspaceTurn();
            if (!current(token) || app.workspaceDraftIdentity?.draftId !== previous) return;
            await app.attachWorkspaceDraft(draftId);
            if (!current(token)) return;
            status.textContent = 'Saved map opened.';
            close();
        } catch (error) {
            if (current(token)) status.textContent = error instanceof Error ? error.message : 'The saved map could not be opened.';
        } finally { busy = false; }
    };
    const onDraftChanged = () => { if (browser || panel) close(); };
    app.addEventListener('honua-workspace-draft-change', onDraftChanged);
    toggle.addEventListener('click', () => {
        if (browser || panel) { close(); return; }
        const session = app.workspaceCookieBinding;
        if (!app.isConnected || !host.isConnected || !session?.principalId || !session?.environmentId) {
            status.textContent = 'Connect to an environment before opening a saved map.'; return;
        }
        binding = authority(); auth = app.auth;
        const token = ++generation;
        browser = document.createElement('honua-studio-content-browser');
        browser.auth = auth;
        browser.addEventListener('honua-studio-open-item', async event => {
            if (!current(token) || busy || event.detail?.family !== 'map') return;
            if (event.detail.draftId) { void attach(event.detail.draftId, token); return; }
            if (typeof event.detail.itemId !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(event.detail.itemId)) return;
            const previous = app.workspaceDraftIdentity?.draftId;
            busy = true;
            try {
                await app.prepareWorkspaceTurn();
                if (!current(token) || app.workspaceDraftIdentity?.draftId !== previous || !browser?.isConnected) return;
                browser.hidden = true;
                panel = document.createElement('honua-studio-lifecycle-panel');
                panel.auth = auth; panel.itemId = event.detail.itemId;
                const selectedPanel = panel;
                panel.addEventListener('honua-studio-lifecycle-activity', activity => {
                    if (current(token) && selectedPanel === panel && selectedPanel.isConnected && activity.detail?.kind === 'version-reopened')
                        void attach(activity.detail.draftId, token);
                });
                section.append(panel);
            } catch (error) {
                if (current(token)) status.textContent = error instanceof Error ? error.message : 'The saved map could not be opened.';
            } finally { busy = false; }
        });
        section.append(browser);
        toggle.textContent = 'Close saved maps'; toggle.setAttribute('aria-expanded', 'true');
    });
    return {
        sessionChanged() {
            if ((browser || panel) && (authority() !== binding || app.auth !== auth)) {
                close(); status.textContent = 'The Studio session changed. Reopen saved maps.';
            }
        },
        dispose() { disposed = true; app.removeEventListener('honua-workspace-draft-change', onDraftChanged); close(); section.remove(); }
    };
}
