import assert from 'node:assert/strict';
import { test } from 'node:test';
import { createConsoleBrowserTransport } from '../../src/Honua.Console.Web/wwwroot/js/console-browser-transport.mjs';

function fixture(overrides = {}) {
    let session = { revision: 1, principalId: 'operator', authenticated: true,
        csrfToken: 'csrf', expiresAt: null, ...overrides };
    const calls = [];
    const transport = createConsoleBrowserTransport({
        apiBaseUrl: 'https://console.example/', applicationBaseUrl: 'https://console.example/console/',
        getSession: async () => session,
        request: async (...args) => { calls.push(args); return new Response('ok'); },
        ...overrides.options,
    });
    return { transport, calls, setSession: value => { session = value; } };
}

test('same-origin mutations use current CSRF and discard caller credentials', async () => {
    const { transport, calls } = fixture();
    const captured = await transport.capture();
    await captured.fetch('api/v1/admin/metadata/layers/12/suggest-style', { method: 'POST',
        headers: { Authorization: 'Bearer stolen', 'X-API-Key': 'key', Cookie: 'cookie', 'X-CSRF-Token': 'old' },
        credentials: 'include', redirect: 'follow' });
    const [url, init] = calls[0];
    assert.equal(url, 'https://console.example/api/v1/admin/metadata/layers/12/suggest-style');
    assert.equal(init.credentials, 'same-origin');
    assert.equal(init.redirect, 'error');
    assert.equal(init.headers.get('X-Honua-CSRF'), 'csrf');
    for (const name of ['Authorization', 'X-API-Key', 'Cookie', 'X-CSRF-Token']) assert.equal(init.headers.get(name), null);
    assert.equal(captured.principalId, 'operator');
});

test('cross-origin requests require a live bearer and omit cookies', async () => {
    const options = { apiBaseUrl: 'https://api.example/base/' };
    const { transport, calls } = fixture({ options, accessToken: 'token', expiresAt: new Date(Date.now() + 60000).toISOString() });
    await (await transport.capture()).fetch('api/v1/admin/auth/session');
    assert.equal(calls[0][1].credentials, 'omit');
    assert.equal(calls[0][1].headers.get('Authorization'), 'Bearer token');
    const missing = fixture({ options });
    await assert.rejects(async () => (await missing.transport.capture()).fetch('api/v1/admin/auth/session'), /bearer/i);
    assert.equal(missing.calls.length, 0);
});

test('rejects URL escapes before dispatch', async () => {
    const { transport, calls } = fixture({ options: { apiBaseUrl: 'https://console.example/base/' } });
    const captured = await transport.capture();
    for (const path of ['https://other.example/api', '//other.example/api', '../api', '/api', 'api#token', 'api/../../escape', 'api/%2e%2e/%2e%2e/escape', 'api\\escape', 'api/%2e%2e%2fescape']) {
        await assert.rejects(() => captured.fetch(path), /route|API/);
    }
    assert.equal(calls.length, 0);
});

test('expired or changed authority rejects dispatch and late responses', async () => {
    const { transport, calls, setSession } = fixture();
    const captured = await transport.capture();
    setSession({ revision: 2, principalId: 'other', authenticated: true });
    await assert.rejects(() => captured.fetch('api/v1/admin/auth/session'), /session changed/i);
    assert.equal(calls.length, 0);
    const expired = fixture({ expiresAt: new Date(0).toISOString() });
    await assert.rejects(() => expired.transport.capture(), /sign in/i);
    let resolve;
    const late = fixture({ options: { request: () => new Promise(done => { resolve = done; }) } });
    const binding = await late.transport.capture();
    const result = binding.fetch('api/v1/admin/auth/session');
    while (!resolve) await Promise.resolve();
    late.setSession({ revision: 2, principalId: 'operator', authenticated: true });
    resolve(new Response('old authority'));
    await assert.rejects(() => result, /session changed/i);
});

test('each preview keeps its own session and a root API does not authorize Console routes', async () => {
    const options = { apiBaseUrl: 'https://console.example/', applicationBaseUrl: 'https://console.example/console/' };
    const signedIn = fixture({ options, accessToken: 'token', expiresAt: new Date(Date.now() + 60000).toISOString() });
    const first = await signedIn.transport.captureResourceAuthorization();
    const style = first('https://console.example/api/styles/7.json');
    assert.equal(style.api, true);
    assert.equal(style.credentials, 'same-origin');
    assert.equal(style.headers.Authorization, 'Bearer token');
    const proxy = first('https://console.example/console/map-proxy/env/styles/7.json');
    assert.equal(proxy.api, false);
    assert.equal(proxy.credentials, 'same-origin');
    assert.equal(proxy.headers.Authorization, undefined);
    const external = first('https://tile.openstreetmap.org/1/2/3.png');
    assert.equal(external.api, false);
    assert.deepEqual(external.headers, {});
    signedIn.transport.publishSession({ revision: 2, principalId: 'other', authenticated: true, accessToken: 'next', expiresAt: new Date(Date.now() + 60000).toISOString() });
    assert.throws(() => first('https://console.example/tiles/7/0/0/0.mvt'), /session changed/i);
    signedIn.setSession({ revision: 2, principalId: 'other', authenticated: true, accessToken: 'next', expiresAt: new Date(Date.now() + 60000).toISOString() });
    const reopened = await signedIn.transport.captureResourceAuthorization();
    assert.equal(reopened('https://console.example/api/styles/7.json').headers.Authorization, 'Bearer next');
});

test('a capture that resolves after a newer sign-in does not restore the old bearer', async () => {
    let release;
    const gate = new Promise(done => { release = done; });
    let session = { revision: 1, principalId: 'operator', authenticated: true, accessToken: 'old', expiresAt: new Date(Date.now() + 60000).toISOString() };
    const transport = createConsoleBrowserTransport({
        apiBaseUrl: 'https://api.example/base/',
        applicationBaseUrl: 'https://console.example/console/',
        getSession: () => gate.then(() => session),
    });
    const pending = transport.captureResourceAuthorization();
    transport.publishSession({ revision: 2, principalId: 'other', authenticated: true, accessToken: 'next', expiresAt: session.expiresAt });
    release();
    const authorize = await pending;
    assert.throws(() => authorize('https://api.example/base/api/styles/7.json'), /session changed/i);
});

test('expiry at the same revision reopens public previews and closes the old one', async () => {
    const expiresAt = new Date(Date.now() + 60000).toISOString();
    let session = { revision: 1, principalId: 'operator', authenticated: true, accessToken: 'token', expiresAt };
    const transport = createConsoleBrowserTransport({
        apiBaseUrl: 'https://api.example/base/',
        applicationBaseUrl: 'https://console.example/console/',
        getSession: async () => session,
    });
    const removed = [];
    const stop = transport.subscribeSession((snapshot) => {
        if (!snapshot.authenticated) removed.push('closed');
    });
    const first = await transport.captureResourceAuthorization();
    assert.equal(first('https://api.example/base/scenes/block/tileset.json').headers.Authorization, 'Bearer token');
    session = { revision: 1, principalId: 'operator', authenticated: false, accessToken: null, expiresAt: new Date(0).toISOString() };
    const reopened = await transport.captureResourceAuthorization();
    const pub = reopened('https://api.example/base/scenes/block/tileset.json');
    assert.equal(pub.headers.Authorization, undefined);
    assert.equal(pub.api, true);
    assert.throws(() => first('https://api.example/base/scenes/block/tileset.json'), /session changed/i);
    assert.equal(removed.length, 1);
    stop();
});

test('unsigned public API reads stay anonymous and signed cross-origin reads need a live bearer', async () => {
    const options = { apiBaseUrl: 'https://api.example/base/', applicationBaseUrl: 'https://console.example/console/' };
    const anonymous = fixture({ options, authenticated: false, accessToken: null, principalId: null, csrfToken: null });
    const pub = (await anonymous.transport.captureResourceAuthorization())('https://api.example/base/scenes/block/tileset.json');
    assert.equal(pub.headers.Authorization, undefined);
    assert.equal(pub.credentials, 'omit');
    const signedOut = fixture({ options, accessToken: null });
    const authorize = await signedOut.transport.captureResourceAuthorization();
    assert.throws(() => authorize('https://api.example/base/scenes/block/tileset.json'), /bearer/i);
});

test('missing CSRF and pre-cancelled requests never dispatch', async () => {
    const missing = fixture({ csrfToken: null });
    await assert.rejects(async () => (await missing.transport.capture()).fetch('api/v1/admin/test', { method: 'POST' }), /CSRF/);
    assert.equal(missing.calls.length, 0);
    const cancelled = fixture();
    const signal = AbortSignal.abort();
    await assert.rejects(async () => (await cancelled.transport.capture()).fetch('api/v1/admin/test', { signal }), { name: 'AbortError' });
    assert.equal(cancelled.calls.length, 0);
});

test('public session assertions do not expose credentials', async () => {
    const { transport } = fixture({ accessToken: 'token', expiresAt: new Date(Date.now() + 60000).toISOString() });
    const captured = await transport.capture();
    assert.deepEqual(Object.keys(captured).sort(), ['assertCurrent', 'fetch', 'principalId', 'revision']);
    assert.equal(await captured.assertCurrent(), undefined);
});
