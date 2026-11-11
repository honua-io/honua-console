import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';
import { fetchRows } from '../../src/Honua.Console.Shell/wwwroot/chart-preview.js';
import { bindMapSession, init, requireMapWorkerGuard } from '../../src/Honua.Console.Shell/wwwroot/map-preview.js';

const guardUrl = new URL('../../src/Honua.Console.Shell/wwwroot/map-fetch-guard.js', import.meta.url);

test('an authorized map fetch keeps overrides and refuses redirects', async () => {
    const calls = [];
    const original = async (input, init) => {
        calls.push([input, init]);
        return new Response(null, { status: 204 });
    };
    const request = new Request('https://api.example/base/api/redirect.json', {
        headers: { Authorization: 'Bearer new' },
        redirect: 'follow',
    });
    await globalThis.guardedAuthorizedFetch(original, request);
    assert.equal(calls[0][0].redirect, 'error');
    assert.equal(calls[0][0].headers.get('Authorization'), 'Bearer new');
    assert.equal(calls[0][0].url, 'https://api.example/base/api/redirect.json');
    assert.equal(calls[0][1], undefined);

    const controller = new AbortController();
    const plainRequest = new Request('https://api.example/base/tiles/1/2/3.mvt', { redirect: 'follow' });
    await globalThis.guardedAuthorizedFetch(original, plainRequest, {
        method: 'POST',
        headers: { Authorization: 'Bearer next', 'X-Custom': 'yes' },
        body: 'payload',
        signal: controller.signal,
    });
    const sent = calls[1][0];
    assert.equal(sent.method, 'POST');
    assert.equal(sent.redirect, 'error');
    assert.equal(sent.headers.get('Authorization'), 'Bearer next');
    assert.equal(sent.headers.get('X-Custom'), 'yes');
    assert.equal(await sent.text(), 'payload');
    controller.abort();
    assert.equal(sent.signal.aborted, true);

    const plain = new Request('https://tile.openstreetmap.org/1/2/3.png', { redirect: 'follow' });
    await globalThis.guardedAuthorizedFetch(original, plain);
    assert.equal(calls[2][0], plain);
    assert.equal(calls[2][1], undefined);

    const posted = new Request('https://api.example/base/api/query', { method: 'POST', body: 'kept' });
    await globalThis.guardedAuthorizedFetch(original, posted);
    assert.equal(calls[3][0], posted);
    assert.equal(calls[3][1], undefined);
    assert.equal(await posted.text(), 'kept');

    await globalThis.guardedAuthorizedFetch(original, 'https://api.example/plain', { method: 'POST', body: 'plain' });
    assert.equal(calls[4][0], 'https://api.example/plain');
    assert.equal(calls[4][1].method, 'POST');
    assert.equal(calls[4][1].body, 'plain');
});

test('the worker script installs one fetch guard', async () => {
    const seen = [];
    const context = {
        fetch: async (input, init) => { seen.push([input, init]); return new Response(null, { status: 204 }); },
        Request,
        Headers,
        Response,
        URL,
    };
    vm.createContext(context);
    vm.runInContext(readFileSync(guardUrl, 'utf8'), context);
    const request = new context.Request('https://api.example/base/api/styles/7.json', {
        headers: { Authorization: 'Bearer worker' },
        redirect: 'follow',
    });
    await context.fetch(request);
    assert.equal(seen[0][0].redirect, 'error');
    assert.equal(seen[0][0].headers.get('Authorization'), 'Bearer worker');
    const posted = new context.Request('https://api.example/base/api/query', { method: 'POST', body: 'kept' });
    await context.fetch(posted);
    assert.equal(seen[1][0], posted);
    assert.equal(await posted.text(), 'kept');
    assert.equal(typeof context.guardedAuthorizedFetch, 'function');
    assert.equal(context.__honuaAuthorizedFetch, true);
});

test('a blob copy of map-preview resolves its guard before the module loads', async () => {
    const sourceUrl = new URL('../../src/Honua.Console.Shell/wwwroot/map-preview.js', import.meta.url);
    const source = readFileSync(sourceUrl, 'utf8');
    const specifier = source.match(/^import '(\.\/[^']+)';/m);
    assert.ok(specifier, 'map-preview must keep one relative guard import for the page and the worker');
    const unresolved = `data:text/javascript,${encodeURIComponent(source)}`;
    await assert.rejects(() => import(unresolved), /ERR_UNSUPPORTED_RESOLVE_REQUEST|map-fetch-guard/);
    const dependency = new URL(specifier[1], sourceUrl).href;
    const resolved = source.replace(specifier[0], `import '${dependency}';`);
    const loaded = await import(`data:text/javascript,${encodeURIComponent(resolved)}`);
    assert.equal(typeof loaded.init, 'function');
    assert.equal(globalThis.__honuaAuthorizedFetch, true);
});

test('authenticated map init fails when the worker guard cannot be installed', async () => {
    const calls = [];
    await assert.rejects(() => requireMapWorkerGuard({ prewarm() { calls.push('prewarm'); } }), /workers cannot be guarded/);
    assert.deepEqual(calls, []);
    await assert.rejects(
        () => requireMapWorkerGuard({
            prewarm() { calls.push('prewarm'); },
            importScriptInWorkers() { return Promise.reject(new Error('blocked')); },
        }),
        /blocked/,
    );
    assert.deepEqual(calls, ['prewarm']);
    const hrefs = [];
    await requireMapWorkerGuard({
        prewarm() { calls.push('ready'); },
        importScriptInWorkers(href) { hrefs.push(href); return Promise.resolve(); },
    });
    assert.deepEqual(calls, ['prewarm', 'ready']);
    assert.equal(hrefs.length, 1);
    assert.equal(new URL(hrefs[0]).pathname.endsWith('/map-fetch-guard.js'), true);

    let constructed = false;
    const previousWindow = globalThis.window;
    const previousDocument = globalThis.document;
    const previousCapture = globalThis.honuaConsoleCaptureResourceAuthorization;
    globalThis.document = {};
    globalThis.window = {
        maplibregl: {
            Map: class { constructor() { constructed = true; } },
            prewarm() {},
            importScriptInWorkers: () => Promise.reject(new Error('blocked')),
            ScaleControl: class {},
            NavigationControl: class {},
        },
    };
    const authorize = () => ({ url: 'https://api.example/api/styles/7.json', credentials: 'omit', headers: {} });
    authorize.current = () => true;
    globalThis.honuaConsoleCaptureResourceAuthorization = async () => authorize;
    try {
        assert.equal(await init({ id: 'map' }, { styleUrl: 'https://api.example/api/styles/7.json' }), false);
        assert.equal(constructed, false);
    } finally {
        globalThis.window = previousWindow;
        globalThis.document = previousDocument;
        globalThis.honuaConsoleCaptureResourceAuthorization = previousCapture;
    }
});

test('a session change removes the map before a late response can be applied', () => {
    let listener;
    const map = { removed: false, remove() { this.removed = true; } };
    let revision = 1;
    const authorize = () => ({ url: 'https://api.example/base/api/styles/7.json', api: true, credentials: 'omit', headers: {} });
    authorize.current = () => revision === 1;
    bindMapSession(map, authorize, (fn) => {
        listener = fn;
        return () => { listener = null; };
    });
    revision = 2;
    listener();
    assert.equal(map.removed, true);
    assert.equal(listener, null);
});

test('feature rows are dropped when the session changes while the body is read', async () => {
    let revision = 1;
    const authorize = (url) => {
        if (revision !== 1) throw new Error('The Console session changed. Reopen this view before continuing.');
        return { url, api: true, credentials: 'omit', headers: { Authorization: 'Bearer token' } };
    };
    let release;
    const gate = new Promise((done) => { release = done; });
    const previous = globalThis.fetch;
    globalThis.fetch = async () => ({
        ok: true,
        json: () => gate.then(() => ({ features: [{ attributes: { name: 'old' } }] })),
    });
    try {
        const pending = fetchRows('https://api.example/base/rest/services/1/FeatureServer/0/query', authorize);
        revision = 2;
        release();
        assert.deepEqual(await pending, []);
    } finally {
        globalThis.fetch = previous;
    }
});
