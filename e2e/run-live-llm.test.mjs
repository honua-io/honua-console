import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, readFileSync, rmSync, symlinkSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { evaluateNoProviderExit } from './run-live-llm.mjs';

test('fails closed when provider is none and HONUA_LIVE_LLM_REQUIRE_PROVIDER is true', () => {
  assert.equal(evaluateNoProviderExit('none', { HONUA_LIVE_LLM_REQUIRE_PROVIDER: 'true' }), 2);
  assert.equal(evaluateNoProviderExit('none', { HONUA_LIVE_LLM_REQUIRE_PROVIDER: '1' }), 2);
});

test('allows explicit gated mode when HONUA_LIVE_LLM_REQUIRE_PROVIDER is false or unset', () => {
  assert.equal(evaluateNoProviderExit('none', { HONUA_LIVE_LLM_REQUIRE_PROVIDER: 'false' }), 0);
  assert.equal(evaluateNoProviderExit('none', {}), 0);
});

test('does not block when a live provider resolved', () => {
  assert.equal(evaluateNoProviderExit('bedrock', { HONUA_LIVE_LLM_REQUIRE_PROVIDER: 'true' }), null);
  assert.equal(evaluateNoProviderExit('openai', { HONUA_LIVE_LLM_REQUIRE_PROVIDER: 'true' }), null);
});

test('reports missing required credentials when launched through a symlink', (t) => {
  const testDir = mkdtempSync(join(tmpdir(), 'honua-live-llm-'));
  t.after(() => rmSync(testDir, { recursive: true, force: true }));
  const runner = join(testDir, 'run-live-llm.mjs');
  const entrypoint = join(testDir, 'linked-runner.mjs');
  const summary = join(testDir, 'summary.md');
  copyFileSync(new URL('./run-live-llm.mjs', import.meta.url), runner);
  symlinkSync(runner, entrypoint, 'file');

  const result = spawnSync(process.execPath, [entrypoint], {
    encoding: 'utf8',
    env: {
      HONUA_LIVE_LLM_MODE: 'none',
      HONUA_LIVE_LLM_REQUIRE_PROVIDER: 'true',
      GITHUB_STEP_SUMMARY: summary,
    },
  });

  assert.equal(result.status, 2, result.stderr);
  assert.match(readFileSync(summary, 'utf8'), /NEEDS_CREDENTIAL_CONFIG/);
});
