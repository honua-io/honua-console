import assert from 'node:assert/strict';
import { afterEach, test } from 'node:test';

afterEach(() => {
    delete globalThis.honuaConsoleCaptureResourceAuthorization;
    delete globalThis.honuaConsoleApiBase;
    delete globalThis.window;
    delete globalThis.document;
});

let revision = 0;
async function fixture() {
    const viewers = [];
    const pending = [];
    const scripts = [];
    const Cesium = {
        Viewer: class {
            constructor(element) {
                this.element = element;
                this.destroyed = false;
                this.scene = { primitives: { add: (tile) => { this.tile = tile; } } };
                this.imageryLayers = [];
                viewers.push(this);
            }
            zoomTo() { return Promise.resolve(); }
            destroy() { this.destroyed = true; this.tile?.destroy(); }
            isDestroyed() { return this.destroyed; }
        },
        Cesium3DTileset: { fromUrl: (url) => new Promise((resolve) => pending.push({ url, resolve })) },
    };
    globalThis.window = {
        location: { href: 'https://console.example/', origin: 'https://console.example' },
        Cesium,
    };
    globalThis.document = {
        querySelector: () => null,
        createElement: () => ({ setAttribute() {} }),
        head: { appendChild: (node) => { if (node.src) scripts.push(node); } },
    };
    const module = await import(`../../src/Honua.Console.Shell/wwwroot/scene-viewer.js?lifecycle=${++revision}`);
    return { module, viewers, pending, scripts, Cesium };
}
const flush = () => new Promise((resolve) => setImmediate(resolve));
const tile = () => ({ tilesLoaded: true, root: { content: {} }, destroy() { this.destroyed = true; } });

for (const failure of ['error', 'missing global', 'append exception']) {
    test(`scene runtime recovers after ${failure}`, async () => {
        const f = await fixture();
        delete window.Cesium;
        if (failure === 'append exception') document.head.appendChild = () => { throw new Error('blocked'); };
        const first = f.module.init({}, '/scenes/a/tileset.json', 'env');
        if (failure === 'error') f.scripts[0].onerror();
        if (failure === 'missing global') f.scripts[0].onload();
        assert.equal(await first, false);
        document.head.appendChild = (node) => { if (node.src) f.scripts.push(node); };
        const second = f.module.init({}, '/scenes/a/tileset.json', 'env');
        assert.equal(f.scripts.length, failure === 'append exception' ? 1 : 2);
        window.Cesium = f.Cesium;
        f.scripts.at(-1).onload();
        await flush();
        f.pending[0].resolve(tile());
        assert.equal(await second, true);
    });
}

test('disposing during runtime loading never constructs a viewer', async () => {
    const f = await fixture();
    delete window.Cesium;
    const element = {};
    const initializing = f.module.init(element, '/scenes/a/tileset.json', 'env');
    f.module.dispose(element);
    window.Cesium = f.Cesium;
    f.scripts[0].onload();
    await flush();
    assert.equal(f.viewers.length, 0);
    assert.equal(await initializing, false);
    assert.equal(f.module.inspect(element), null);
});

test('disposing during tileset loading destroys the pending viewer and late tileset', async () => {
    const f = await fixture();
    const element = {};
    const initializing = f.module.init(element, '/scenes/a/tileset.json', 'env');
    await flush();
    f.module.dispose(element);
    assert.equal(f.viewers[0].destroyed, true);
    const late = tile();
    f.pending[0].resolve(late);
    assert.equal(await initializing, false);
    assert.equal(late.destroyed, true);
    assert.equal(f.module.inspect(element), null);
});

test('a late older tileset cannot replace the newer scene', async () => {
    const f = await fixture();
    const element = {};
    const older = f.module.init(element, '/scenes/old/tileset.json', 'env');
    await flush();
    const newer = f.module.init(element, '/scenes/new/tileset.json', 'env');
    await flush();
    f.pending[1].resolve(tile());
    assert.equal(await newer, true);
    const late = tile();
    f.pending[0].resolve(late);
    assert.equal(await older, false);
    assert.equal(f.viewers[0].destroyed, true);
    assert.equal(late.destroyed, true);
    assert.match(f.module.inspect(element).tilesetUrl, /scenes\/new\//);
    f.module.dispose(element);
    assert.equal(f.viewers[1].destroyed, true);
});

for (const action of ['dispose', 'replace']) {
    test(`${action} during authorization cannot construct the old viewer`, async () => {
        const f = await fixture();
        let authorize;
        globalThis.honuaConsoleCaptureResourceAuthorization = () => new Promise((resolve) => { authorize = resolve; });
        const element = {};
        const older = f.module.init(element, '/scenes/old/tileset.json', 'env');
        await flush();
        assert.equal(f.module.inspect(element), null);
        if (action === 'dispose') f.module.dispose(element);
        delete globalThis.honuaConsoleCaptureResourceAuthorization;
        const newer = action === 'replace' ? f.module.init(element, '/scenes/new/tileset.json', 'env') : null;
        authorize(null);
        assert.equal(await older, false);
        if (newer) {
            await flush();
            assert.equal(f.viewers.length, 1);
            f.pending[0].resolve(tile());
            assert.equal(await newer, true);
            assert.match(f.module.inspect(element).tilesetUrl, /scenes\/new\//);
        } else {
            assert.equal(f.viewers.length, 0);
        }
        f.module.dispose(element);
    });

    test(`${action} during camera movement cannot resurrect the old scene`, async () => {
        const f = await fixture();
        let finishZoom;
        f.Cesium.Viewer.prototype.zoomTo = () => new Promise((resolve) => { finishZoom = resolve; });
        const element = {};
        const older = f.module.init(element, '/scenes/old/tileset.json', 'env');
        await flush();
        const oldTile = tile();
        f.pending[0].resolve(oldTile);
        await flush();
        assert.equal(f.module.inspect(element), null);
        if (action === 'dispose') f.module.dispose(element);
        f.Cesium.Viewer.prototype.zoomTo = () => Promise.resolve();
        const newer = action === 'replace' ? f.module.init(element, '/scenes/new/tileset.json', 'env') : null;
        finishZoom();
        assert.equal(await older, false);
        assert.equal(f.viewers[0].destroyed, true);
        assert.equal(oldTile.destroyed, true);
        if (newer) {
            await flush();
            f.pending[1].resolve(tile());
            assert.equal(await newer, true);
            assert.match(f.module.inspect(element).tilesetUrl, /scenes\/new\//);
        }
        f.module.dispose(element);
    });
}

test('runtime loading is shared by active elements without coupling disposal', async () => {
    const f = await fixture();
    delete window.Cesium;
    const first = {};
    const second = {};
    const abandoned = f.module.init(first, '/scenes/first/tileset.json', 'env');
    const active = f.module.init(second, '/scenes/second/tileset.json', 'env');
    assert.equal(f.scripts.length, 1);
    f.module.dispose(first);
    window.Cesium = f.Cesium;
    f.scripts[0].onload();
    assert.equal(await abandoned, false);
    await flush();
    assert.equal(f.viewers.length, 1);
    assert.equal(f.viewers[0].element, second);
    f.pending[0].resolve(tile());
    assert.equal(await active, true);
    f.module.dispose(second);
});

test('completed scenes are destroyed before their replacements bind', async () => {
    const f = await fixture();
    const element = {};
    const first = f.module.init(element, '/scenes/first/tileset.json', 'env');
    await flush();
    f.pending[0].resolve(tile());
    assert.equal(await first, true);
    const second = f.module.init(element, '/scenes/second/tileset.json', 'env');
    assert.equal(f.viewers[0].destroyed, true);
    assert.equal(f.module.inspect(element), null);
    await flush();
    f.pending[1].resolve(tile());
    assert.equal(await second, true);
    f.module.dispose(element);
    f.module.dispose(element);
    assert.equal(f.viewers[1].destroyed, true);
});

test('a failed replacement leaves no partial inspectable instance', async () => {
    const f = await fixture();
    const element = {};
    const first = f.module.init(element, '/scenes/first/tileset.json', 'env');
    await flush();
    f.pending[0].resolve(tile());
    assert.equal(await first, true);
    f.Cesium.Cesium3DTileset.fromUrl = () => Promise.reject(new Error('unavailable'));
    assert.equal(await f.module.init(element, '/scenes/second/tileset.json', 'env'), false);
    assert.equal(f.viewers[0].destroyed, true);
    assert.equal(f.viewers[1].destroyed, true);
    assert.equal(f.module.inspect(element), null);
});

for (const failure of ['dispose', 'tileset rejection', 'viewer exception']) {
    test(`scene request hook is released after ${failure}`, async () => {
        const f = await fixture();
        const original = () => {};
        f.Cesium.Resource = class {
            static _Implementations = { loadWithXhr: original };
            constructor(options) { Object.assign(this, options); }
        };
        globalThis.honuaConsoleCaptureResourceAuthorization = async () => (url) => ({
            api: true, url, headers: {}, credentials: 'same-origin',
        });
        if (failure === 'viewer exception') f.Cesium.Viewer = class {
            constructor() { throw new Error('no WebGL'); }
        };
        if (failure === 'tileset rejection') f.Cesium.Cesium3DTileset.fromUrl = () => Promise.reject(new Error('offline'));
        const element = {};
        const initializing = f.module.init(element, '/scenes/a/tileset.json', 'env');
        await flush();
        if (failure === 'dispose') {
            assert.notEqual(f.Cesium.Resource._Implementations.loadWithXhr, original);
            f.module.dispose(element);
            f.module.dispose(element);
            assert.equal(f.Cesium.Resource._Implementations.loadWithXhr, original);
            f.pending[0].resolve(tile());
        }
        assert.equal(await initializing, false);
        assert.equal(f.Cesium.Resource._Implementations.loadWithXhr, original);
        assert.equal(f.module.inspect(element), null);
    });
}
