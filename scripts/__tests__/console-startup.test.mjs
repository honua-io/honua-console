import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

const startup = await readFile(new URL('../../src/Honua.Console.Web/wwwroot/js/console-startup.js', import.meta.url), 'utf8');

for (const [language, expected] of [
  ['en-us@posix', 'en-US'],
  ['zz-ZZ', 'en-US'],
  ['', 'en-US'],
  [undefined, 'en-US'],
  ['fr-fr', 'fr-FR'],
  ['ja-JP', 'ja-JP'],
]) {
  test(`startup chooses ${expected} for browser language ${String(language)}`, () => {
    const starts = [];
    const warnings = [];
    runInNewContext(startup, {
      navigator: { languages: [language], language },
      Blazor: { start: options => starts.push(options.applicationCulture) },
      console: { warn: message => warnings.push(message) },
    });
    assert.deepEqual(starts, [expected]);
    assert.deepEqual(warnings, expected === 'en-US'
      ? ['Unsupported browser locale; starting Honua Console with en-US.'] : []);
  });
}

function startWith(fetchResponses) {
  const calls = [];
  const delays = [];
  let options;
  runInNewContext(startup, {
    navigator: { languages: ['en-US'], language: 'en-US' },
    Blazor: { start: value => { options = value; } },
    console: { warn() {} },
    fetch: (url, init) => {
      calls.push({ url, init });
      const next = fetchResponses.shift();
      return next instanceof Error ? Promise.reject(next) : Promise.resolve(next);
    },
    setTimeout: (callback, ms) => { delays.push(ms); callback(); return 0; },
  });
  return { options, calls, delays };
}

test('startup retries a boot resource whose download fails transiently', async () => {
  const ok = { ok: true, status: 200 };
  const { options, calls, delays } = startWith([new TypeError('Failed to fetch'), { ok: false, status: 503 }, ok]);
  const response = await options.loadBootResource('assembly', 'Honua.Console.Shell.abc.wasm',
    '/console/_framework/Honua.Console.Shell.abc.wasm', 'sha256-x', 'assembly');
  assert.equal(response, ok);
  assert.equal(calls.length, 3);
  assert.deepEqual(delays, [500, 1000]);
  for (const call of calls) {
    assert.equal(call.url, '/console/_framework/Honua.Console.Shell.abc.wasm');
    assert.equal(call.init.integrity, 'sha256-x');
    assert.equal(call.init.cache, 'no-cache');
  }
});

test('startup gives up after five attempts and reports the last failure', async () => {
  const failure = new TypeError('Failed to fetch');
  const { options, calls, delays } = startWith([failure, failure, failure, failure, failure]);
  await assert.rejects(options.loadBootResource('dotnetwasm', 'dotnet.native.wasm',
    '/console/_framework/dotnet.native.wasm', '', 'dotnetwasm'), failure);
  assert.equal(calls.length, 5);
  assert.deepEqual(delays, [500, 1000, 2000, 4000]);
  assert.equal('integrity' in calls[0].init, false);
});

test('startup releases the body of a transient response before retrying', async () => {
  let cancelled = 0;
  const ok = { ok: true, status: 200 };
  const unavailable = { ok: false, status: 503, body: { cancel: () => { cancelled++; return Promise.reject(new Error('gone')); } } };
  const { options } = startWith([unavailable, ok]);
  assert.equal(await options.loadBootResource('assembly', 'a.wasm', '/console/_framework/a.wasm', '', 'assembly'), ok);
  assert.equal(cancelled, 1);
});

test('startup returns the last transient HTTP failure once its attempts run out', async () => {
  const unavailable = { ok: false, status: 503 };
  const { options, calls } = startWith([unavailable, unavailable, unavailable, unavailable, unavailable]);
  assert.equal(await options.loadBootResource('pdb', 'x.pdb', '/console/_framework/x.pdb', '', 'pdb'), unavailable);
  assert.equal(calls.length, 5);
});

test('startup returns a definite HTTP failure without retrying it', async () => {
  const missing = { ok: false, status: 404 };
  const { options, calls } = startWith([missing]);
  assert.equal(await options.loadBootResource('globalization', 'icudt.dat', '/console/_framework/icudt.dat', '', 'icu'), missing);
  assert.equal(calls.length, 1);
});

test('startup leaves JavaScript modules and configuration to the default loader', () => {
  const { options, calls } = startWith([]);
  for (const type of ['dotnetjs', 'configuration', 'manifest']) {
    assert.equal(options.loadBootResource(type, 'x', '/console/_framework/x', '', type), undefined);
  }
  assert.equal(calls.length, 0);
});

test('index.html gives the loading and boot-failure states a document title', async () => {
  const html = await readFile(new URL('../../src/Honua.Console.Web/wwwroot/index.html', import.meta.url), 'utf8');
  assert.match(html, /<head>[\s\S]*<title>Honua Console<\/title>[\s\S]*<\/head>/);
});
