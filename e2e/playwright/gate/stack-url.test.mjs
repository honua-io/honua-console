import test from 'node:test';
import assert from 'node:assert/strict';
import { gateConsoleUrl } from './stack-url.mjs';

test('stack input resolves shipping Console without losing its path base', () => {
  for (const path of ['', '/', '/console', '/console/']) {
    assert.equal(gateConsoleUrl(`https://example.test${path}`), 'https://example.test/console/');
  }
  assert.equal(gateConsoleUrl('http://localhost:18080'), 'http://localhost:18080/console/');
});
test('rejects missing, credential-bearing and ambiguous stack inputs', () => {
  for (const input of [undefined, '', 'not a url', 'file:///console/', 'https://user:pass@example.test',
    'https://example.test/?token=x', 'https://example.test/#token', 'https://example.test/studio']) {
    assert.throws(() => gateConsoleUrl(input));
  }
});
