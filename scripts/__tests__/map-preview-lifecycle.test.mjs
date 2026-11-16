import assert from 'node:assert/strict';
import { test } from 'node:test';

function deferred() {
    let resolve;
    const promise = new Promise(done => { resolve = done; });
    return { promise, resolve };
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
                maps.push(this);
                if (phase === 'constructed') queueMicrotask(() => onConstruct());
            }
            addControl() { if (phase === 'control-error') throw new Error('control failed'); }
            on() {}
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
        names.forEach((name, i) => {
            if (previous[i]) Object.defineProperty(globalThis, name, previous[i]);
            else delete globalThis[name];
        });
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
