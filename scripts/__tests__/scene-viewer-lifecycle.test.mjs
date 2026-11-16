import assert from 'node:assert/strict';
import { test } from 'node:test';

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
            destroy() { this.destroyed = true; }
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

