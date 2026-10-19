/** Human actions over the embedded Studio's canonical draft lifecycle. */
import { mountStudioPublicationControls } from './studio-publication-controls.mjs';

export function mountStudioVersionControls({ app, host, status }) {
    const publication = mountStudioPublicationControls({ app, host, status });
    const section = document.createElement('section');
    section.setAttribute('aria-label', 'Saved map versions');
    section.dataset.studioVersions = '';
    const actions = document.createElement('div');
    actions.className = 'console-action-row';
    const save = document.createElement('button');
    save.type = 'button'; save.textContent = 'Save version';
    save.className = 'console-button';
    const browse = document.createElement('button');
    browse.type = 'button'; browse.textContent = 'Saved versions';
    browse.className = 'console-button console-button-secondary';
    const versions = document.createElement('ul');
    versions.setAttribute('aria-label', 'Versions of this map');
    actions.append(save, browse);
    section.append(actions, versions);
    const canvas = host.querySelector('[data-studio-mount]');
    if (canvas) canvas.before(section); else host.append(section);
    let busy = false, disposed = false, selectedContext;
    const context = () => JSON.stringify([app.workspaceCookieBinding, app.workspaceDraftIdentity?.draftId]);
    const active = captured => !disposed && host.isConnected && app.isConnected && context() === captured;
    function update() {
        const draft = app.workspaceDraftIdentity?.draftId;
        const currentContext = context();
        if (currentContext !== selectedContext) { versions.replaceChildren(); selectedContext = currentContext; }
        save.disabled = browse.disabled = busy || !draft;
        for (const button of versions.querySelectorAll('button')) button.disabled = busy;
    }
    async function run(action, complete) {
        if (busy || !app.workspaceDraftIdentity || disposed || !host.isConnected || !app.isConnected) return;
        const captured = context();
        busy = true; update();
        try {
            const result = await action();
            if (active(captured)) complete?.(result, captured);
        } catch (error) {
            if (active(captured)) status.textContent = error instanceof Error ? error.message : 'The version action failed.';
        } finally {
            busy = false;
            if (!disposed) update();
        }
    }
    save.addEventListener('click', () => run(() => app.saveWorkspaceVersion(), version => {
        status.textContent = `Saved version ${version.versionNumber}.`;
        versions.replaceChildren();
    }));
    browse.addEventListener('click', () => run(() => app.listWorkspaceVersions(), (items, captured) => {
        versions.replaceChildren();
        for (const version of items) {
            const row = document.createElement('li');
            const label = document.createElement('span');
            label.textContent = `Version ${version.versionNumber} `;
            const reopen = document.createElement('button');
            reopen.type = 'button'; reopen.textContent = 'Reopen as draft';
            reopen.className = 'console-button console-button-secondary console-button-small';
            reopen.setAttribute('aria-label', `Reopen version ${version.versionNumber} as draft`);
            reopen.addEventListener('click', () => {
                if (!active(captured) || !versions.contains(reopen)) return;
                void run(() => app.reopenWorkspaceVersion(version.versionId));
            });
            const publish = document.createElement('button');
            publish.type = 'button'; publish.textContent = 'Review publication';
            publish.className = 'console-button console-button-secondary console-button-small';
            publish.setAttribute('aria-label', `Review publication of version ${version.versionNumber}`);
            publish.addEventListener('click', () => {
                if (!active(captured) || !versions.contains(publish)) return;
                void publication.review(version.versionId);
            });
            row.append(label, reopen);
            if (typeof app.reviewWorkspacePublication === 'function' && typeof app.publishWorkspaceVersion === 'function') row.append(publish);
            versions.append(row);
        }
        if (!items.length) status.textContent = 'This map has no saved versions yet.';
    }));
    app.addEventListener('honua-workspace-draft-change', update);
    update();
    return {
        dispose() {
            disposed = true;
            app.removeEventListener('honua-workspace-draft-change', update);
            publication.dispose();
            section.remove();
        }
    };
}
