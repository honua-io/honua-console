import test from 'node:test';
import assert from 'node:assert/strict';
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
