import assert from 'node:assert/strict';
import { test } from 'node:test';

for (const failure of ['network', 'missing-runtime', 'append']) {
    test(`a preview retries loading MapLibre after ${failure} failure`, async () => {
        const names = ['window', 'document'];
        const previous = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
        const scripts = [];
        let appendAttempts = 0;
        globalThis.window = {};
        globalThis.document = {
            baseURI: 'https://console.example/console/',
            querySelector() { return {}; },
            createElement() { return {}; },
            head: {
                appendChild(script) {
                    appendAttempts++;
                    if (failure === 'append' && appendAttempts === 1) throw new Error('temporarily unavailable');
                    scripts.push(script);
                    queueMicrotask(() => failure === 'missing-runtime' ? script.onload() : script.onerror());
                },
            },
        };
        try {
            const { init } = await import(`../../src/Honua.Console.Shell/wwwroot/map-preview.js?recovery=${failure}`);
            const options = { styleUrl: 'https://api.example/styles/layer.json' };
            assert.equal(await init({}, options), false);
            assert.equal(await init({}, options), false);
            assert.equal(appendAttempts, 2, 'the next preview must request the runtime again');
            assert.equal(scripts.length, failure === 'append' ? 1 : 2);
        } finally {
            names.forEach((name, index) => {
                if (previous[index]) Object.defineProperty(globalThis, name, previous[index]);
                else delete globalThis[name];
            });
        }
    });
}

test('concurrent previews share a retry and mount after the runtime recovers', async () => {
    const names = ['window', 'document'];
    const previous = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
    const scripts = [];
    const maps = [];
    const containers = [{}, {}];
    let module;
    globalThis.window = {};
    globalThis.document = {
        baseURI: 'https://console.example/console/',
        querySelector() { return {}; },
        createElement() { return {}; },
        head: { appendChild(script) { scripts.push(script); } },
    };
    try {
        module = await import('../../src/Honua.Console.Shell/wwwroot/map-preview.js?recovery=concurrent');
        const options = { styleUrl: 'https://api.example/styles/layer.json' };
        const failed = containers.map(container => module.init(container, options));
        assert.equal(scripts.length, 1);
        scripts[0].onerror();
        assert.deepEqual(await Promise.all(failed), [false, false]);
        const recovered = containers.map(container => module.init(container, options));
        assert.equal(scripts.length, 2);
        globalThis.window.maplibregl = {
            NavigationControl: class {},
            Map: class {
                constructor() { maps.push(this); }
                addControl() {}
                on() {}
                remove() { this.removed = true; }
            },
        };
        scripts[1].onload();
        assert.deepEqual(await Promise.all(recovered), [true, true]);
        assert.equal(maps.length, 2);
        const extra = {};
        containers.push(extra);
        assert.equal(await module.init(extra, options), true);
        assert.equal(scripts.length, 2, 'successful runtime stays cached');
    } finally {
        containers.forEach(container => module?.dispose(container));
        names.forEach((name, index) => {
            if (previous[index]) Object.defineProperty(globalThis, name, previous[index]);
            else delete globalThis[name];
        });
    }
});


test('a cached runtime retries a failed stylesheet without duplicating pending or loaded links', async () => {
    const names = ['window', 'document'];
    const previous = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
    const links = [];
    const containers = [{}, {}, {}, {}];
    let currentLink = null;
    let module;
    globalThis.window = { maplibregl: {
        NavigationControl: class {},
        Map: class { addControl() {} on() {} remove() {} },
    } };
    globalThis.document = {
        baseURI: 'https://console.example/console/',
        querySelector() { return currentLink; },
        createElement() { return { setAttribute() {}, remove() { currentLink = null; } }; },
        head: { appendChild(link) { links.push(link); currentLink = link; } },
    };
    try {
        module = await import('../../src/Honua.Console.Shell/wwwroot/map-preview.js?recovery=stylesheet');
        const options = { styleUrl: 'https://api.example/styles/layer.json' };
        assert.equal(await module.init(containers[0], options), true);
        assert.equal(await module.init(containers[1], options), true);
        assert.equal(links.length, 1, 'pending stylesheet is shared');
        assert.equal(links[0].href, 'https://console.example/console/_content/Honua.Console.Shell/vendor/maplibre-gl/maplibre-gl.css');
        links[0].onerror();
        assert.equal(currentLink, null, 'failed stylesheet must not block recovery');
        assert.equal(await module.init(containers[2], options), true);
        assert.equal(links.length, 2, 'next preview retries even when runtime is cached');
        assert.equal(await module.init(containers[3], options), true);
        assert.equal(links.length, 2, 'usable stylesheet stays shared');
    } finally {
        containers.forEach(container => module?.dispose(container));
        names.forEach((name, index) => {
            if (previous[index]) Object.defineProperty(globalThis, name, previous[index]);
            else delete globalThis[name];
        });
    }
});
