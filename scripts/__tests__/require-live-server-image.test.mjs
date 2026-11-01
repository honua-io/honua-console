import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const script = new URL('../require-live-server-image.mjs', import.meta.url);
const check = image => spawnSync(process.execPath, [fileURLToPath(script)], {
  encoding: 'utf8', env: { ...process.env, HONUA_CONSOLE_E2E_SERVER_IMAGE: image },
});

test('live CI rejects absent, mutable and foreign server images', () => {
  for (const image of ['', 'ghcr.io/honua-io/honua-server:nightly-aot',
    `ghcr.io/other/server@sha256:${'a'.repeat(64)}`, 'ghcr.io/honua-io/honua-server@sha256:abc']) {
    const result = check(image);
    assert.notEqual(result.status, 0, image);
    assert.match(result.stderr, /immutable honua-server GHCR digest/);
  }
  const pinned = `ghcr.io/honua-io/honua-server@sha256:${'a'.repeat(64)}`;
  assert.equal(check(pinned).status, 0);
  assert.equal(check(pinned.replace('@', ':candidate@')).status, 0);
});
