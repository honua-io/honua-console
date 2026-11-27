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
        assert.equal(calls[0].ast, true);
        dispose(target);
    });
});

test('a spec whose data is a URL shows the fallback without embedding', async () => {
    await withChartModule('url', async ({ init, dispose }) => {
        let embeds = 0;
        Object.assign(window, { vega: {}, vegaLite: {}, vegaEmbed: async () => { embeds++; return { finalize() {} }; } });
        const target = container();

        const mounted = await init(target, { spec: JSON.stringify({ data: { url: 'https://data.example/rows.json' }, mark: 'bar' }) });

        assert.equal(mounted, false);
        assert.equal(embeds, 0);
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

test('nested composition and lookup URLs are stripped while inline data records stay intact', async () => {
    await withChartModule('strip-urls', async ({ init, dispose }) => {
        let embeddedSpec = null;
        Object.assign(window, {
            vega: {},
            vegaLite: {},
            vegaEmbed: async (_target, chart) => {
                embeddedSpec = chart;
                return { finalize() {} };
            },
        });
        const target = container();
        const spec = JSON.stringify({
            data: { values: [{ data: { url: 'district-a' }, amount: 12 }] },
            datasets: { rows: [{ data: { url: 'district-b' }, amount: 5 }] },
            transform: [{ lookup: 'id', from: { data: { url: '/map-proxy/secret' }, key: 'id' } }],
            layer: [{ data: { url: '/api/agent-workspace/session' }, mark: 'bar' }],
            concat: [{ data: { url: 'https://evil.example/exfil' }, mark: 'point' }],
            hconcat: [{ data: { url: '/api/hconcat' }, mark: 'line' }],
            vconcat: [{ data: { url: '/api/vconcat' }, mark: 'area' }],
            facet: { data: { url: '/api/facet' }, spec: { data: { url: '/api/facet-spec' }, mark: 'bar' } },
            repeat: { spec: { data: { url: '/api/repeat' }, mark: 'bar' } },
        });

        assert.equal(await init(target, { spec }), true);
        assert.ok(embeddedSpec);
        assert.equal(embeddedSpec.data.values[0].data.url, 'district-a');
        assert.equal(embeddedSpec.datasets.rows[0].data.url, 'district-b');
        assert.equal(embeddedSpec.transform[0].from.data.url, undefined);
        assert.equal(embeddedSpec.layer[0].data.url, undefined);
        assert.equal(embeddedSpec.concat[0].data.url, undefined);
        assert.equal(embeddedSpec.hconcat[0].data.url, undefined);
        assert.equal(embeddedSpec.vconcat[0].data.url, undefined);
        assert.equal(embeddedSpec.facet.data.url, undefined);
        assert.equal(embeddedSpec.facet.spec.data.url, undefined);
        assert.equal(embeddedSpec.repeat.spec.data.url, undefined);
        dispose(target);
    });
});
