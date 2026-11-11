import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { mapRequestParameters } from '../../src/Honua.Console.Shell/wwwroot/map-preview.js';
import { approveSceneRequest, guardSceneLoad, sameOriginTilesetUrl, sceneViewHeader } from '../../src/Honua.Console.Shell/wwwroot/scene-viewer.js';

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
    guardSceneLoad((url, _responseType, _method, _data, headers, result) => {
        calls.push({ url, authorization: headers.Authorization, view: headers[sceneViewHeader] });
        result.resolve('loaded');
    }, lookup)('https://external.example/tile.b3dm', '', 'GET', null, { ...inherited, [sceneViewHeader]: 'view-a' }, deferred);
    assert.equal(calls[0].authorization, undefined);
    assert.equal(calls[0].view, undefined);
    assert.equal(deferred.value, 'loaded');
    revision = 2;
    const late = {};
    late.resolve = (value) => { late.value = value; };
    late.reject = (error) => { late.error = error; };
    guardSceneLoad((_url, _responseType, _method, _data, _headers, result) => {
        result.resolve('late');
    }, lookup)('https://api.example/base/scenes/block/next.b3dm', '', 'GET', null, { [sceneViewHeader]: 'view-a' }, late);
    assert.equal(late.value, undefined);
    assert.match(String(late.error), /session changed/);
});

test('a scene request keeps its own view when another view has a newer session', () => {
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
    const owned = {};
    owned.resolve = (value) => { owned.value = value; };
    owned.reject = (error) => { owned.error = error; };
    guardSceneLoad((_url, _responseType, _method, _data, headers, result) => {
        assert.equal(headers.Authorization, 'Bearer next');
        assert.equal(headers[sceneViewHeader], undefined);
        result.resolve('loaded');
    }, lookup)('https://api.example/base/scenes/b.b3dm', '', 'GET', null, { [sceneViewHeader]: 'b' }, owned);
    assert.equal(owned.value, 'loaded');
    older = 1;
    const late = {};
    late.resolve = (value) => { late.value = value; };
    late.reject = (error) => { late.error = error; };
    guardSceneLoad((_url, _responseType, _method, _data, _headers, result) => {
        older = 2;
        result.resolve('late');
    }, lookup)('https://api.example/base/scenes/a.b3dm', '', 'GET', null, { [sceneViewHeader]: 'a' }, late);
    assert.equal(late.value, undefined);
    assert.match(String(late.error), /session changed/);
});

test('without an API base, scene paths stay on the environment proxy', () => {
    delete globalThis.honuaConsoleApiBase;
    globalThis.window = { location: { href: 'https://console.example/console/', origin: 'https://console.example' } };
    assert.equal(
        sameOriginTilesetUrl('/scenes/block/tileset.json', 'env b'),
        'https://console.example/scene-proxy/env%20b/scenes/block/tileset.json');
    assert.throws(() => sameOriginTilesetUrl('/scenes/block/tileset.json', ''), /environment/);
    delete globalThis.window;
});
