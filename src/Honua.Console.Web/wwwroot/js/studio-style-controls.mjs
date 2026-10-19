// The embedded Studio owns style edits, persistence and unsaved-work protection.
// Console exposes that lifecycle without keeping a second style document.
export function mountStudioStyleControls({ app, host, status }) {
    const section = document.createElement('div');
    section.className = 'console-action-row';
    section.dataset.studioStyleControls = '';
    const toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'console-button console-button-secondary';
    section.append(toggle);
    const canvas = host.querySelector('[data-studio-mount]');
    if (canvas) canvas.before(section); else host.append(section);

    let busy = false, disposed = false;
    const context = () => JSON.stringify([app.workspaceCookieBinding, app.workspaceDraftIdentity?.draftId]);
    const panel = () => app.shadowRoot?.querySelector('[aria-label="Map style editor"]');
    const active = captured => !disposed && host.isConnected && app.isConnected && context() === captured;
    function update() {
        const open = Boolean(panel());
        toggle.textContent = open ? 'Return to map' : 'Edit map style';
        toggle.setAttribute('aria-pressed', String(open));
        toggle.disabled = busy || !app.workspaceDraftIdentity?.draftId
            || typeof app.openStyleEditor !== 'function' || typeof app.closeStyleEditor !== 'function';
    }
    toggle.addEventListener('click', async () => {
        if (busy || disposed || !host.isConnected || !app.isConnected || toggle.disabled) return;
        const captured = context(), closing = Boolean(panel());
        busy = true; update();
        try {
            if (closing) await app.closeStyleEditor();
            else await app.openStyleEditor();
            if (!active(captured)) return;
            status.textContent = closing ? 'Map canvas opened.' : 'Editing this map’s style.';
            if (!closing && document.activeElement === toggle)
                panel()?.querySelector('select:not(:disabled)')?.focus();
        } catch (error) {
            if (active(captured)) status.textContent = error instanceof Error ? error.message : 'The style editor could not be opened.';
        } finally {
            busy = false;
            if (!disposed) update();
        }
    });
    const observer = new MutationObserver(update);
    if (app.shadowRoot) observer.observe(app.shadowRoot, { childList: true, subtree: true });
    app.addEventListener('honua-workspace-draft-change', update);
    app.addEventListener('honua-studio-style-dirty-change', update);
    update();
    return {
        dispose() {
            disposed = true;
            observer.disconnect();
            app.removeEventListener('honua-workspace-draft-change', update);
            app.removeEventListener('honua-studio-style-dirty-change', update);
            section.remove();
        }
    };
}
