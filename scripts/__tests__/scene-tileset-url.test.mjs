import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sameOriginTilesetUrl } from '../../src/Honua.Console.Shell/wwwroot/scene-viewer.js';

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

test('without an API base, scene paths stay on the environment proxy', () => {
    delete globalThis.honuaConsoleApiBase;
    globalThis.window = { location: { href: 'https://console.example/console/', origin: 'https://console.example' } };
    assert.equal(
        sameOriginTilesetUrl('/scenes/block/tileset.json', 'env b'),
        'https://console.example/scene-proxy/env%20b/scenes/block/tileset.json');
    assert.throws(() => sameOriginTilesetUrl('/scenes/block/tileset.json', ''), /environment/);
    delete globalThis.window;
});
