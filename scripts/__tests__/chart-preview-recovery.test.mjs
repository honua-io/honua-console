import assert from 'node:assert/strict';
import { test } from 'node:test';

const spec = JSON.stringify({ data: { values: [{ category: 'east', amount: 12 }] }, mark: 'bar' });
const container = () => ({
    children: [], style: {},
    appendChild(child) { this.children.push(child); },
    replaceChildren() { this.children = []; },
});

async function fixture(name, run) {
    const names = ['window', 'document', 'fetch', 'honuaConsoleCaptureResourceAuthorization'];
    const saved = names.map(name => Object.getOwnPropertyDescriptor(globalThis, name));
    const scripts = [];
    const results = [];
    globalThis.window = {};
    globalThis.document = {
        baseURI: 'https://console.example/console/',
        createElement: container,
        head: { appendChild(script) { scripts.push(script); } },
    };
    delete globalThis.honuaConsoleCaptureResourceAuthorization;
    const embed = async (target, chart) => {
        const result = { chart, finalized: 0, finalize() { this.finalized++; } };
        results.push(result);
        target.appendChild(chart);
        return result;
    };
    try {
        const module = await import(`../../src/Honua.Console.Shell/wwwroot/chart-preview.js?recovery=${name}`);
        await run({ ...module, scripts, results, embed });
    } finally {
        names.forEach((name, index) => saved[index]
            ? Object.defineProperty(globalThis, name, saved[index]) : delete globalThis[name]);
    }
}

for (const peer of ['vega', 'vegaLite', 'vegaEmbed']) {
    test(`chart retries after transient ${peer} script failure`, async () => {
        await fixture(peer, async ({ init, dispose, scripts, embed }) => {
            for (const earlier of ['vega', 'vegaLite', 'vegaEmbed']) {
                if (earlier === peer) break;
                window[earlier] = {};
            }
            document.head.appendChild = script => {
                scripts.push(script);
                queueMicrotask(() => script.onerror());
            };
            const target = container();
            assert.equal(await init(target, { spec }), false);
            document.head.appendChild = script => {
                scripts.push(script);
                queueMicrotask(() => {
                    window.vega = {};
                    window.vegaLite = {};
                    window.vegaEmbed = embed;
                    script.onload();
                });
            };
            assert.equal(await init(target, { spec }), true);
            assert.equal(scripts.length, 2);
            dispose(target);
        });
    });
}

for (const failure of ['append', 'missing-runtime']) {
    test(`chart retries after ${failure} runtime failure`, async () => {
        await fixture(failure, async ({ init, dispose, embed }) => {
            Object.assign(window, { vega: {}, vegaLite: {} });
            document.head.appendChild = script => {
                if (failure === 'append') throw new Error('temporarily unavailable');
                queueMicrotask(() => script.onload());
            };
            const target = container();
            assert.equal(await init(target, { spec }), false);
            document.head.appendChild = script => {
                window.vegaEmbed = embed;
                queueMicrotask(() => script.onload());
            };
            assert.equal(await init(target, { spec }), true);
            dispose(target);
        });
    });
}

test('concurrent chart retries share the loader and keep the recovered runtime cached', async () => {
    await fixture('concurrent', async ({ init, dispose, scripts, embed }) => {
        Object.assign(window, { vega: {}, vegaLite: {} });
        const targets = [container(), container()];
        const failed = targets.map(target => init(target, { spec }));
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(scripts.length, 1);
        scripts[0].onerror();
        assert.deepEqual(await Promise.all(failed), [false, false]);
        const retried = targets.map(target => init(target, { spec }));
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(scripts.length, 2);
        window.vegaEmbed = embed;
        scripts[1].onload();
        assert.deepEqual(await Promise.all(retried), [true, true]);
        assert.equal(await init(targets[0], { spec }), true);
        assert.equal(scripts.length, 2);
        targets.forEach(dispose);
    });
});

for (const phase of ['authorization', 'rows', 'runtime', 'embed']) {
    test(`disposed chart stays empty when pending ${phase} finishes`, async () => {
        await fixture(phase, async ({ init, dispose, scripts, results, embed }) => {
            let resume;
            let started;
            const gate = new Promise(resolve => { resume = resolve; });
            const pendingPhase = new Promise(resolve => { started = resolve; });
            Object.assign(window, { vega: {}, vegaLite: {}, vegaEmbed: embed });
            if (phase === 'authorization') {
                globalThis.honuaConsoleCaptureResourceAuthorization = async () => {
                    started(); await gate; return null;
                };
            } else if (phase === 'rows') {
                globalThis.fetch = async () => {
                    started(); await gate;
                    return { ok: true, json: async () => ({ features: [{ attributes: { category: 'late' } }] }) };
                };
            } else if (phase === 'runtime') {
                delete window.vegaEmbed;
                document.head.appendChild = script => { scripts.push(script); started(); };
            } else {
                window.vegaEmbed = async (...args) => { started(); await gate; return embed(...args); };
            }
            const target = container();
            const pending = init(target, { spec, ...(phase === 'rows' ? { featuresUrl: '/features' } : {}) });
            await pendingPhase;
            dispose(target);
            if (phase === 'runtime') {
                window.vegaEmbed = embed;
                scripts[0].onload();
            }
            resume();
            assert.equal(await pending, false);
            assert.equal(target.children.length, 0);
            for (const result of results) assert.equal(result.finalized, 1);
        });
    });
}

test('superseded chart initialization cannot replace or dispose the newer chart', async () => {
    await fixture('superseded', async ({ init, dispose, results, embed }) => {
        let resume;
        let started;
        const gate = new Promise(resolve => { resume = resolve; });
        const firstStarted = new Promise(resolve => { started = resolve; });
        let calls = 0;
        Object.assign(window, {
            vega: {}, vegaLite: {},
            vegaEmbed: async (...args) => {
                if (++calls === 1) { started(); await gate; }
                return embed(...args);
            },
        });
        const target = container();
        const first = init(target, { spec });
        await firstStarted;
        assert.equal(await init(target, { spec: JSON.stringify({ data: { values: [{ category: 'new' }] }, mark: 'point' }) }), true);
        const currentChildren = [...target.children];
        resume();
        assert.equal(await first, false);
        assert.deepEqual(target.children, currentChildren);
        assert.equal(results[0].finalized, 0);
        assert.equal(results[1].finalized, 1);
        dispose(target);
        assert.equal(results[0].finalized, 1);
    });
});
