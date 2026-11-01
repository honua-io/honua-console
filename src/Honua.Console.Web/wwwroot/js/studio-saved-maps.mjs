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
    let browser, panel, binding, auth, busy = false, disposed = false;
    const authority = () => JSON.stringify(app.workspaceCookieBinding);
    const current = () => !disposed && host.isConnected && app.isConnected && authority() === binding && app.auth === auth;
    const close = () => {
        browser?.remove(); panel?.remove(); browser = panel = undefined;
        toggle.textContent = 'Open saved maps'; toggle.setAttribute('aria-expanded', 'false');
    };
    const attach = async draftId => {
        if (!current() || busy || typeof draftId !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(draftId)) return;
        const previous = app.workspaceDraftIdentity?.draftId;
        busy = true;
        try {
            await app.prepareWorkspaceTurn();
            if (!current() || app.workspaceDraftIdentity?.draftId !== previous) return;
            await app.attachWorkspaceDraft(draftId);
            if (!current()) return;
            status.textContent = 'Saved map opened.';
            close();
        } catch (error) {
            if (current()) status.textContent = error instanceof Error ? error.message : 'The saved map could not be opened.';
        } finally { busy = false; }
    };
    toggle.addEventListener('click', () => {
        if (browser || panel) { close(); return; }
        const session = app.workspaceCookieBinding;
        if (!app.isConnected || !host.isConnected || !session?.principalId || !session?.environmentId) {
            status.textContent = 'Connect to an environment before opening a saved map.'; return;
        }
        binding = authority(); auth = app.auth;
        browser = document.createElement('honua-studio-content-browser');
        browser.auth = auth;
        browser.addEventListener('honua-studio-open-item', async event => {
            if (!current() || busy || event.detail?.family !== 'map') return;
            if (event.detail.draftId) { void attach(event.detail.draftId); return; }
            if (typeof event.detail.itemId !== 'string' || !/^[A-Za-z0-9_-]{1,128}$/.test(event.detail.itemId)) return;
            const previous = app.workspaceDraftIdentity?.draftId;
            busy = true;
            try {
                await app.prepareWorkspaceTurn();
                if (!current() || app.workspaceDraftIdentity?.draftId !== previous || !browser?.isConnected) return;
                browser.hidden = true;
                panel = document.createElement('honua-studio-lifecycle-panel');
                panel.auth = auth; panel.itemId = event.detail.itemId;
                panel.addEventListener('honua-studio-lifecycle-activity', activity => {
                    if (activity.detail?.kind === 'version-reopened') void attach(activity.detail.draftId);
                });
                section.append(panel);
            } catch (error) {
                if (current()) status.textContent = error instanceof Error ? error.message : 'The saved map could not be opened.';
            } finally { busy = false; }
        });
        section.append(browser);
        toggle.textContent = 'Close saved maps'; toggle.setAttribute('aria-expanded', 'true');
    });
    return { dispose() { disposed = true; close(); section.remove(); } };
}
