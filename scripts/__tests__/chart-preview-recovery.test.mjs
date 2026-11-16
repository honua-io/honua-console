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

