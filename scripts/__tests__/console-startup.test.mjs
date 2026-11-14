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
