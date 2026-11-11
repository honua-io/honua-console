import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fetchRows } from '../../src/Honua.Console.Shell/wwwroot/chart-preview.js';
import { bindMapSession, guardedAuthorizedFetch } from '../../src/Honua.Console.Shell/wwwroot/map-preview.js';

test('an authorized map fetch does not follow a redirect off the API', async () => {
    const calls = [];
    const original = async (input) => {
        calls.push(input);
        return new Response(null, { status: 302, headers: { Location: 'https://api.example/outside/style.json' } });
    };
    const request = new Request('https://api.example/base/api/redirect.json', {
        headers: { Authorization: 'Bearer new' },
        redirect: 'follow',
    });
    await guardedAuthorizedFetch(original, request);
    assert.equal(calls[0].redirect, 'error');
    assert.equal(calls[0].headers.get('Authorization'), 'Bearer new');
    assert.equal(calls[0].url, 'https://api.example/base/api/redirect.json');
    const plain = new Request('https://tile.openstreetmap.org/1/2/3.png', { redirect: 'follow' });
    await guardedAuthorizedFetch(original, plain);
    assert.equal(calls[1], plain);
    const worker = readFileSync(new URL('../../src/Honua.Console.Shell/wwwroot/map-fetch-guard.js', import.meta.url), 'utf8');
    assert.match(worker, /redirect: 'error'/);
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
