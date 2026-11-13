import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createConsoleBrowserTransport } from '../../src/Honua.Console.Web/wwwroot/js/console-browser-transport.mjs';
import {
    readStudioSession, StudioSignInRequired, STUDIO_SESSION_RECHECK_MS,
} from '../../src/Honua.Console.Web/wwwroot/js/studio-session.mjs';

const body = (data) => JSON.stringify({ success: true, data });
const valid = { authenticated: true, principalId: 'user:a@b%2F', environmentId: 'default', sourcePathBase: '', tenantId: 't1' };

function fixture({ status = 200, payload = body(valid), session } = {}) {
    const calls = [];
    const transport = createConsoleBrowserTransport({
        apiBaseUrl: 'https://console.example/', applicationBaseUrl: 'https://console.example/console/',
        getSession: async () => session ?? { revision: 1, principalId: 'operator', authenticated: true, csrfToken: 'csrf', expiresAt: null },
        request: async (...args) => { calls.push(args); return new Response(payload, { status }); },
    });
    return { capture: () => transport.capture(), calls };
}

test('reads the native Studio session through the Console transport', async () => {
    const { capture, calls } = fixture();
    const session = await readStudioSession({ capture, now: () => 0 });
    assert.equal(calls.length, 1);
    assert.equal(calls[0][0], 'https://console.example/api/v1/studio/session');
    assert.equal(calls[0][1].credentials, 'same-origin');
    assert.equal(session.principalId, valid.principalId);
    assert.equal(session.environmentId, 'default');
    assert.equal(session.environmentName, 'default');
    assert.equal(session.sourcePathBase, '');
    assert.equal(session.tenantId, 't1');
    assert.equal(session.expiresAt, new Date(STUDIO_SESSION_RECHECK_MS).toISOString());
    assert.equal(typeof session.transport.fetch, 'function');
    assert.ok(Object.isFrozen(session));
    assert.equal('csrfToken' in session, false, 'the transport owns CSRF; the session must not carry it');
});

test('signed-out Console asks for sign-in without calling the API', async () => {
    const { capture, calls } = fixture({ session: { revision: 1, principalId: '', authenticated: false, expiresAt: null } });
    await assert.rejects(readStudioSession({ capture }), StudioSignInRequired);
    assert.equal(calls.length, 0);
});

for (const status of [401, 403]) {
    test(`native ${status} asks for sign-in`, async () => {
        const { capture } = fixture({ status, payload: '' });
        await assert.rejects(readStudioSession({ capture }), StudioSignInRequired);
    });
}

test('server failure is not reported as a sign-in problem', async () => {
    const { capture } = fixture({ status: 503, payload: '{}' });
    await assert.rejects(readStudioSession({ capture }), error => !(error instanceof StudioSignInRequired) && /503/.test(error.message));
});

for (const [name, data] of [
    ['unauthenticated body', { ...valid, authenticated: false }],
    ['missing principal', { ...valid, principalId: '' }],
    ['invalid environment', { ...valid, environmentId: '../x' }],
    ['trailing slash source base', { ...valid, sourcePathBase: '/honua/' }],
    ['missing source base', { ...valid, sourcePathBase: undefined }],
]) {
    test(`rejects ${name}`, async () => {
        const { capture } = fixture({ payload: body(data) });
        await assert.rejects(readStudioSession({ capture }), /invalid/);
    });
}

test('omits a non-string tenant', async () => {
    const { capture } = fixture({ payload: body({ ...valid, tenantId: undefined }) });
    assert.equal((await readStudioSession({ capture })).tenantId, undefined);
});

test('rejects authority changes while the bootstrap body is consumed',async()=>{
 let current=true;
 const capture=async()=>({fetch:async()=>({ok:true,status:200,json:async()=>{current=false;return {data:valid};}}),
  assertCurrent:async()=>{if(!current) throw new Error('Console session changed');}});
 await assert.rejects(readStudioSession({capture}),/session changed/);
});

test('explicit legacy host bootstraps cookie authority without initializing native transport',async()=>{
 const data={...valid,csrfToken:'csrf',expiresAt:new Date(60000).toISOString()};let sent;
 const session=await readStudioSession({mode:'legacy',now:()=>0,capture:()=>assert.fail('native capture'),
  request:async(...args)=>{sent=args;return Response.json(data);}});
 assert.equal(sent[0],'/api/agent-workspace/session');
 assert.equal(sent[1].credentials,'same-origin');assert.equal(sent[1].redirect,'error');
 assert.equal(session.csrfToken,'csrf');assert.equal(session.transport,undefined);assert.ok(Object.isFrozen(session));
});

test('native capture failures never fall back to the legacy host',async()=>{
 await assert.rejects(readStudioSession({capture:async()=>{throw new Error('signed out');},
  request:()=>assert.fail('legacy fallback')}),StudioSignInRequired);
});
