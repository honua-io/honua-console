import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { StudioDraftCreateSession } from '../../src/Honua.Console.Web/wwwroot/js/studio-draft-create.mjs';
import * as workspaceModule from '../../src/Honua.Console.Web/wwwroot/js/agent-workspace.mjs';
import { createStudioCookieSession, sameCookieAuthority } from '../../src/Honua.Console.Web/wwwroot/js/cookie-workspace-transport.mjs';

const source = await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/studio-embed.mjs', import.meta.url), 'utf8');
const mount = source.slice(source.indexOf('async function studioElement('), source.indexOf('\nasync function mount()')).replaceAll('import.meta.url', JSON.stringify('https://console.example/js/studio-embed.mjs'));

for (const tenantId of ['old', 'new', undefined]) {
    test(`retained Studio mount treats tenant ${tenantId} as ${tenantId === 'old' ? 'renewal' : 'new authority'}`, async () => {
        const previous = { principalId: 'actor', environmentId: 'default', sourcePathBase: '', tenantId: 'old' };
        let prepared = 0;
        const sessions = [];
        const app = { workspaceCookieBinding: previous, prepareWorkspaceTurn: async () => { prepared++; } };
        Object.defineProperty(app, 'session', { get: () => sessions.at(-1), set: value => sessions.push(value) });
        const context = vm.createContext({
            getAgentWorkspace: () => undefined,
            elementsByEnvironment: new Map([['default', app]]), window: {},
            createStudioCookieSession, sameCookieAuthority,
        });
        context.nextSession = { ...previous, tenantId, expiresAt: new Date(Date.now() + 60000).toISOString() };
        await vm.runInContext(mount + ';studioElement(nextSession)', context);
        assert.equal(sessions.length, tenantId === 'old' ? 1 : 2);
        if (tenantId !== 'old') assert.equal(sessions[0], undefined, 'reset released bundle before renewing across tenants');
        assert.equal(app.workspaceAuthorityChanged, tenantId !== 'old');
        assert.equal(prepared, tenantId === 'old' ? 0 : 1);
        assert.equal(app.workspaceCookieBinding.tenantId, tenantId);
        assert.equal(app.session.tenantId, tenantId);
        assert.equal(sameCookieAuthority(previous, app.workspaceCookieBinding), tenantId === 'old');
    });
}


test('Studio creates a map without mounting the unavailable conversation drawer', async () => {
    const records = new Map();
    const storage = { getItem: key => records.get(key) ?? null,
        setItem: (key, value) => records.set(key, value) };
    globalThis.window = {};
    globalThis.sessionStorage = storage;
    let calls = 0;
    try {
        // No drawer and no workspace global: the static deployment disables conversation.
        const app = { isConnected: true, createWorkspaceDraft: async (key, commandId) => {
            calls++;
            assert.equal(key, 'new-map');
            assert.equal(commandId, 'create-command');
            return { draftId: 'canonical-map', generation: 0 };
        } };
        const context = vm.createContext({
            elementsByEnvironment: new Map([['default', app]]), window,
            getAgentWorkspace: workspaceModule.getAgentWorkspace,
            createStudioCookieSession, sameCookieAuthority,
            nextSession: { principalId: 'operator', environmentId: 'default', sourcePathBase: '',
                expiresAt: new Date(Date.now() + 60000).toISOString() }
        });
        await vm.runInContext(mount + ';studioElement(nextSession)', context);
        const create = new StudioDraftCreateSession(app, { uuid: () => 'create-command' });
        assert.equal((await create.submit('new-map')).draftId, 'canonical-map');
        assert.equal(calls, 1);
        assert.equal(create.read().status, 'completed');
        assert.equal(app.workspace, window.honuaAgentWorkspace);
        assert.equal(workspaceModule.getAgentWorkspace(), app.workspace, 'drawer and Studio reuse one workspace');
        assert.equal(app.workspace.storage, storage);
        assert.equal(app.workspace.studioBindingProvider(), app.workspaceCookieBinding);
    } finally {
        delete globalThis.window;
        delete globalThis.sessionStorage;
    }
});
