import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs', import.meta.url), 'utf8');
const mount = source.slice(source.indexOf('async function mount()'), source.indexOf('new MutationObserver(mount)'))
    .replaceAll('import.meta.url', JSON.stringify('https://console.example/js/studio-embed.mjs'));

function studioHost() {
    const status = { textContent: '' };
    const slot = { append(app) { app.parent = this; } };
    return {
        isConnected: true,
        dataset: {},
        status,
        slot,
        querySelector: selector => selector === '[data-studio-status]' ? status : slot,
    };
}

function deferred() {
    let resolve;
    const promise = new Promise(complete => { resolve = complete; });
    return { promise, resolve };
}

for (const phase of ['session read', 'editor setup']) {
    test(`reopening Studio during ${phase} keeps the editor on the current page`, async () => {
        const started = deferred();
        const pending = deferred();
        const app = {};
        const controls = [];
        const originalSession = { environmentName: 'Rivers' };
        const reopenedSession = { ...originalSession };
        const original = studioHost();
        let active = original;
        const mountControls = ({ host }) => {
            controls.push(host);
            return { dispose() {} };
        };
        const context = vm.createContext({
            assets: Promise.resolve({ registerAllStudioElements() {} }),
            document: { querySelector: selector => selector === '[data-studio-embed]' ? active : {} },
            location: { pathname: '/studio' },
            element: undefined,
            session: undefined,
            async readStudioSession() {
                if (active !== original) return reopenedSession;
                if (phase === 'session read') {
                    started.resolve();
                    await pending.promise;
                }
                return originalSession;
            },
            async studioElement(studioSession) {
                if (studioSession === originalSession && phase === 'editor setup') {
                    started.resolve();
                    await pending.promise;
                }
                return app;
            },
            mountStudioDraftCreate: mountControls,
            mountStudioSavedMaps: mountControls,
            mountStudioVersionControls: mountControls,
            mountStudioStyleControls: mountControls,
            mountStudioLayerPicker: mountControls,
            StudioSignInRequired: Error,
        });
        const runMount = () => vm.runInContext(mount + '\nmount()', context);
        const oldMount = runMount();
        await started.promise;

        original.isConnected = false;
        active = studioHost();
        await runMount();
        pending.resolve();
        await oldMount;

        assert.equal(app.parent, active.slot, 'the retained editor stays in the reopened host');
        assert.equal(context.session, reopenedSession);
        assert.equal(active.status.textContent, 'Connected to Rivers');
        assert.deepEqual(controls, Array(5).fill(active));
    });
}
