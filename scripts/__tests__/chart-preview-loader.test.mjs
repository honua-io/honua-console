import assert from 'node:assert/strict';
import { test } from 'node:test';

const container = () => ({
    children: [], style: {},
    appendChild(child) { this.children.push(child); },
    replaceChildren() { this.children = []; },
});

async function withChartModule(name, run) {
    const names = ['window', 'document'];
    const saved = names.map(key => Object.getOwnPropertyDescriptor(globalThis, key));
    globalThis.window = {};
    globalThis.document = { baseURI: 'https://console.example/console/', createElement: container, head: { appendChild() {} } };
    try {
        await run(await import(`../../src/Honua.Console.Shell/wwwroot/chart-preview.js?loader=${name}`));
    } finally {
        names.forEach((key, index) => saved[index]
            ? Object.defineProperty(globalThis, key, saved[index]) : delete globalThis[key]);
    }
}

test('charts embed with a loader that refuses every external resource', async () => {
    await withChartModule('embed', async ({ init, dispose, NO_NETWORK_LOADER }) => {
        const calls = [];
        Object.assign(window, {
            vega: {},
            vegaLite: {},
            vegaEmbed: async (target, chart, options) => {
                calls.push(options);
                return { finalize() {} };
            },
        });
        const target = container();
        const spec = JSON.stringify({ data: { values: [{ category: 'east', amount: 12 }] }, mark: 'bar' });

        assert.equal(await init(target, { spec }), true);

        assert.equal(calls.length, 1);
        assert.equal(calls[0].loader, NO_NETWORK_LOADER);
        dispose(target);
    });
});

test('the no-network loader rejects data, link, request and file loads', async () => {
    await withChartModule('refuse', async ({ NO_NETWORK_LOADER }) => {
        assert.ok(Object.isFrozen(NO_NETWORK_LOADER));
        for (const entry of ['load', 'sanitize', 'http', 'file']) {
            await assert.rejects(NO_NETWORK_LOADER[entry]('https://data.example/rows.json', {}), /cannot load external resources/);
        }
    });
});
