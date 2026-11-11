import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { mapRequestParameters } from '../../src/Honua.Console.Shell/wwwroot/map-preview.js';
import { approveSceneRequest, guardSceneLoad, sameOriginTilesetUrl, sceneViewHeader } from '../../src/Honua.Console.Shell/wwwroot/scene-viewer.js';

function adaptSceneLoad(handle) {
    return (...request) => handle({ url: request[0], headers: request[4], result: request[5] });
}

test('configured API scenes stay on that API, including a path prefix', () => {
    globalThis.window = { location: { href: 'https://console.example/console/', origin: 'https://console.example' } };
    globalThis.honuaConsoleApiBase = 'https://api.example/base/';
    assert.equal(
        sameOriginTilesetUrl('https://api.example/base/scenes/block/tileset.json', 'configured-api'),
        'https://api.example/base/scenes/block/tileset.json');
    assert.equal(
        sameOriginTilesetUrl('/scenes/block/tileset.json?v=1', 'configured-api'),
        'https://api.example/base/scenes/block/tileset.json?v=1');
    assert.throws(() => sameOriginTilesetUrl('https://other.example/scenes/block/tileset.json', 'configured-api'), /Honua scene/);
    assert.throws(() => sameOriginTilesetUrl('https://api.example/base/scenes/block/tileset.json#token', 'configured-api'), /fragment/);
    delete globalThis.honuaConsoleApiBase;
    delete globalThis.window;
});

test('scene requests authorize the current session per URL and map requests stay synchronous', () => {
    const source = readFileSync(new URL('../../src/Honua.Console.Shell/wwwroot/map-preview.js', import.meta.url), 'utf8');
    assert.doesNotMatch(source, /transformRequest:\s*async/);
    let revision = 1;
    const authorize = (url) => {
        if (revision !== 1) throw new Error('The Console session changed. Reopen this view before continuing.');
        const api = url.startsWith('https://api.example/base/');
        return {
            url,
            api,
            credentials: api ? 'omit' : 'omit',
            headers: api ? { Authorization: 'Bearer token' } : {},
        };
    };
    const map = mapRequestParameters(authorize, 'https://api.example/base/api/styles/7.json');
    assert.equal(typeof map.then, 'undefined');
    assert.equal(map.headers.Authorization, 'Bearer token');
    const inherited = { Authorization: 'Bearer old', Accept: 'application/json' };
    const external = approveSceneRequest(authorize, 'https://external.example/tile.b3dm', inherited);
    assert.equal(external.headers.Authorization, undefined);
    assert.equal(external.headers.Accept, 'application/json');
    const child = approveSceneRequest(authorize, 'https://api.example/base/scenes/block/next.b3dm', inherited);
    assert.equal(child.headers.Authorization, 'Bearer token');
    const calls = [];
    const deferred = {};
    deferred.resolve = (value) => { deferred.value = value; };
    deferred.reject = (error) => { deferred.error = error; };
    const lookup = () => authorize;
    guardSceneLoad(adaptSceneLoad(({ url, headers, result }) => {
        calls.push({ url, authorization: headers.Authorization, view: headers[sceneViewHeader] });
        result.resolve('loaded');
    }), lookup)('https://external.example/tile.b3dm', '', 'GET', null, { ...inherited, [sceneViewHeader]: 'view-a' }, deferred);
    assert.equal(calls[0].authorization, undefined);
    assert.equal(calls[0].view, undefined);
    assert.equal(deferred.value, 'loaded');
    revision = 2;
    const late = {};
    late.resolve = (value) => { late.value = value; };
    late.reject = (error) => { late.error = error; };
    guardSceneLoad(adaptSceneLoad(({ result }) => {
        result.resolve('late');
    }), lookup)('https://api.example/base/scenes/block/next.b3dm', '', 'GET', null, { [sceneViewHeader]: 'view-a' }, late);
    assert.equal(late.value, undefined);
    assert.match(String(late.error), /session changed/);
});

test('a scene request keeps its own view when another view has a newer session', async () => {
    let older = 1;
    const first = (url) => {
        if (older !== 1) throw new Error('The Console session changed. Reopen this view before continuing.');
        return { url, api: true, credentials: 'omit', headers: { Authorization: 'Bearer old' } };
    };
    const second = (url) => ({ url, api: true, credentials: 'omit', headers: { Authorization: 'Bearer next' } });
    const lookup = (id) => (id === 'a' ? first : second);
    older = 2;
    const borrowed = {};
    borrowed.resolve = (value) => { borrowed.value = value; };
    borrowed.reject = (error) => { borrowed.error = error; };
    let sent = false;
    guardSceneLoad(() => { sent = true; }, lookup)(
        'https://api.example/base/scenes/a.b3dm', '', 'GET', null, { [sceneViewHeader]: 'a', Authorization: 'Bearer old' }, borrowed);
    assert.equal(sent, false);
    assert.equal(borrowed.value, undefined);
    assert.match(String(borrowed.error), /session changed/);
    const owned = deferred();
    await withSceneFetch(async (calls) => {
        guardSceneLoad(() => { throw new Error('authorized scenes do not use the redirecting loader'); }, lookup)(
            'https://api.example/base/scenes/b.b3dm', 'text', 'GET', null, { [sceneViewHeader]: 'b' }, owned);
        await flushSceneLoads();
        assert.equal(calls[0].init.redirect, 'error');
        assert.equal(calls[0].init.credentials, 'omit');
        assert.equal(calls[0].init.headers.Authorization, 'Bearer next');
        assert.equal(calls[0].init.headers[sceneViewHeader], undefined);
    }, () => new Response('loaded', { status: 200 }));
    assert.equal(owned.value, 'loaded');
    older = 1;
    const late = deferred();
    await withSceneFetch(async () => {
        guardSceneLoad(() => { throw new Error('authorized scenes do not use the redirecting loader'); }, lookup)(
            'https://api.example/base/scenes/a.b3dm', 'text', 'GET', null, { [sceneViewHeader]: 'a' }, late);
        await flushSceneLoads();
    }, () => {
        older = 2;
        return new Response('late', { status: 200 });
    });
    assert.equal(late.value, undefined);
    assert.match(String(late.error), /session changed/);
});

test('an authorized scene load refuses redirects and failed responses', async () => {
    const authorize = (url) => ({
        url, api: true, credentials: 'omit', headers: { Authorization: 'Bearer token' },
    });
    const redirected = deferred();
    await withSceneFetch(async (calls) => {
        guardSceneLoad(() => { throw new Error('xhr'); }, () => authorize)(
            'https://api.example/base/scenes/a.json', 'json', 'GET', null, { [sceneViewHeader]: 'view' }, redirected);
        await flushSceneLoads();
        assert.equal(calls[0].url, 'https://api.example/base/scenes/a.json');
        assert.equal(calls[0].init.redirect, 'error');
    }, () => Promise.reject(new TypeError('redirect')));
    assert.match(String(redirected.error), /redirect/);
    const failed = deferred();
    await withSceneFetch(async () => {
        guardSceneLoad(() => { throw new Error('xhr'); }, () => authorize)(
            'https://api.example/base/scenes/missing.json', 'arraybuffer', 'GET', null, { [sceneViewHeader]: 'view' }, failed);
        await flushSceneLoads();
    }, () => new Response('', { status: 404 }));
    assert.match(String(failed.error), /404/);
});

function deferred() {
    const result = {};
    result.resolve = (value) => { result.value = value; };
    result.reject = (error) => { result.error = error; };
    return result;
}

async function withSceneFetch(run, respond) {
    const original = globalThis.fetch;
    const calls = [];
    globalThis.fetch = (url, init) => {
        calls.push({ url, init });
        return Promise.resolve(respond());
    };
    try {
        await run(calls);
    } finally {
        globalThis.fetch = original;
    }
}

function flushSceneLoads() {
    return new Promise((resolve) => setTimeout(resolve, 0));
}

test('without an API base, scene paths stay on the environment proxy', () => {
    delete globalThis.honuaConsoleApiBase;
    globalThis.window = { location: { href: 'https://console.example/console/', origin: 'https://console.example' } };
    assert.equal(
        sameOriginTilesetUrl('/scenes/block/tileset.json', 'env b'),
        'https://console.example/scene-proxy/env%20b/scenes/block/tileset.json');
    assert.throws(() => sameOriginTilesetUrl('/scenes/block/tileset.json', ''), /environment/);
    delete globalThis.window;
});
