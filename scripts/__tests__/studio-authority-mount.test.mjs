import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
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
