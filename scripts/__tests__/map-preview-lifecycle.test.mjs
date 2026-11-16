import assert from 'node:assert/strict';
import { test } from 'node:test';

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
}

function restoreGlobals(names, previous) {
    names.forEach((name, i) => {
        if (previous[i]) Object.defineProperty(globalThis, name, previous[i]);
        else delete globalThis[name];
    });
}

async function fixture(phase, run) {
    const names = ['window', 'document', 'honuaConsoleCaptureResourceAuthorization', 'honuaConsoleSubscribeSession'];
    const previous = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
    const gate = deferred();
    const started = deferred();
    const maps = [];
    let script;
    let releases = 0;
    let captures = 0;
    let onConstruct = () => {};
    const runtime = {
        Map: class {
            constructor(options) {
                this.style = options.style;
                this.removed = 0;
                this.events = new Map();
                this.center = options.center;
                this.zoom = options.zoom;
                maps.push(this);
                if (phase === 'constructed') queueMicrotask(() => onConstruct());
            }
            addControl() { if (phase === 'control-error') throw new Error('control failed'); }
            on(name, callback) { this.events.set(name, callback); }
            off(name) { this.events.delete(name); }
            fire(name) { this.events.get(name)?.(); }
            getStyle() { return { sources: { base: { type: 'raster' } }, layers: [{ id: 'data', type: 'fill' }] }; }
            queryRenderedFeatures() { return this.features ?? []; }
            fitBounds(bounds) {
                this.center = bounds[0].map((value, index) => (value + bounds[1][index]) / 2);
                this.zoom = 16;
            }
            getCenter() { return { toArray: () => this.center }; }
            getZoom() { return this.zoom; }
            easeTo(camera) { this.center = camera.center; this.zoom = camera.zoom; }
            remove() { this.removed++; }
            zoomIn() {}
        },
        NavigationControl: class {},
        prewarm() {},
        async importScriptInWorkers() {
            if (phase === 'worker') { started.resolve(); await gate.promise; }
        },
    };
    const authorize = url => ({ url, credentials: 'omit', headers: {} });
    authorize.current = () => true;
    globalThis.window = phase === 'runtime' ? {} : { maplibregl: runtime };
    globalThis.document = {
        baseURI: 'https://console.example/console/',
        querySelector() { return null; },
        createElement() { return { setAttribute() {} }; },
        head: { appendChild(element) { if ('src' in element) { script = element; started.resolve(); } } },
    };
    globalThis.honuaConsoleCaptureResourceAuthorization = async () => {
        if (++captures === 1 && phase === 'authorization') { started.resolve(); await gate.promise; }
        return authorize;
    };
    globalThis.honuaConsoleSubscribeSession = () => () => { releases++; };
    const module = await import(`../../src/Honua.Console.Shell/wwwroot/map-preview.js?lifecycle=${phase}`);
    try {
        await run({
            ...module, maps, started: started.promise,
            resume() {
                if (phase === 'runtime') { globalThis.window.maplibregl = runtime; script.onload(); }
                gate.resolve();
            },
            releases: () => releases,
            onConstruct(callback) { onConstruct = callback; },
        });
    } finally {
        restoreGlobals(names, previous);
    }
}

for (const phase of ['runtime', 'authorization', 'worker']) {
    test(`dispose cancels initialization waiting for ${phase}`, async () => {
        await fixture(phase, async ({ init, dispose, maps, started, resume }) => {
            const container = {};
            const pending = init(container, { styleUrl: 'https://api.example/styles/old.json' });
            await started;
            dispose(container);
            resume();
            assert.equal(await pending, false);
            assert.equal(maps.length, 0);
        });
    });
}

test('a newer initialization supersedes pending work for the same container', async () => {
    await fixture('authorization', async ({ init, dispose, maps, started, resume, releases }) => {
        const container = {};
        const pending = init(container, { styleUrl: 'https://api.example/styles/old.json' });
        await started;
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/new.json' }), true);
        resume();
        assert.equal(await pending, false);
        assert.equal(maps.length, 1);
        assert.equal(maps[0].style, 'https://api.example/styles/new.json');
        assert.equal(maps[0].removed, 0);
        dispose(container);
        assert.equal(maps[0].removed, 1);
        assert.equal(releases(), 1);
    });
});

test('reinitializing a bound container releases its previous map', async () => {
    await fixture('normal', async ({ init, dispose, maps, releases }) => {
        const container = {};
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/first.json' }), true);
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/second.json' }), true);
        assert.equal(maps.length, 2);
        assert.equal(maps[0].removed, 1);
        assert.equal(releases(), 1);
        dispose(container);
        dispose(container);
        assert.equal(maps[1].removed, 1);
        assert.equal(releases(), 2);
    });
});

test('a failed initialization releases a partially created map', async () => {
    await fixture('control-error', async ({ init, dispose, maps, releases }) => {
        const container = {};
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/first.json' }), false);
        assert.equal(maps.length, 1);
        assert.equal(maps[0].removed, 1);
        assert.equal(releases(), 1);
        dispose(container);
        assert.equal(maps[0].removed, 1);
    });
});

test('disposal after construction releases the map before it can bind', async () => {
    await fixture('constructed', async ({ init, dispose, maps, releases, onConstruct }) => {
        const container = {};
        onConstruct(() => dispose(container));
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/first.json' }), false);
        assert.equal(maps.length, 1);
        assert.equal(maps[0].removed, 1);
        assert.equal(releases(), 1);
    });
});

for (const source of ['extent', 'features']) {
    test(`Recenter restores the fitted ${source} view after panning`, async () => {
        await fixture(`camera-${source}`, async ({ init, recenter, dispose, maps }) => {
            const container = {};
            const options = { styleUrl: 'https://api.example/styles/layer.json', center: [0, 0], zoom: 1 };
            if (source === 'extent') options.bounds = [-124, 32, -114, 42];
            assert.equal(await init(container, options), true);
            const map = maps[0];
            map.features = [{ geometry: { coordinates: [[-124, 32], [-114, 42]] } }];
            map.fire('load');
            map.fire('idle');
            assert.deepEqual(map.center, [-119, 37]);
            map.easeTo({ center: [10, 20], zoom: 4 });
            assert.equal(recenter(container, options.center, options.zoom), true);
            assert.deepEqual(map.center, [-119, 37]);
            assert.equal(map.zoom, 16);
            dispose(container);
        });
    });
}

test('Recenter preserves the initial camera when no feature extent is available', async () => {
    await fixture('camera-empty', async ({ init, recenter, dispose, maps }) => {
        const container = {};
        assert.equal(await init(container, { styleUrl: 'https://api.example/styles/empty.json', center: [0, 0], zoom: 0 }), true);
        const map = maps[0];
        map.fire('load');
        map.fire('idle');
        map.easeTo({ center: [10, 20], zoom: 4 });
        assert.equal(recenter(container, [0, 0], 0), true);
        assert.deepEqual(map.center, [0, 0]);
        assert.equal(map.zoom, 0);
        dispose(container);
        assert.equal(recenter(container, [0, 0], 0), false);
    });
});
