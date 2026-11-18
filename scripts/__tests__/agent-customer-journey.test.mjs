import { test } from 'node:test';
import assert from 'node:assert/strict';
import { exerciseAgentPublication, exerciseAgentRecovery, refuseApiKeyApproval } from '../agent-customer-journey.mjs';

test('recovery refuses a missing or shared approver before stopping Redis', async () => {
  let stopped = false;
  const stopRedis = async () => { stopped = true; };
  for (const approverKey of ['', 'same']) {
    await assert.rejects(exerciseAgentRecovery({ managerUrl: 'http://127.0.0.1:1', proposerKey: 'same', approverKey,
      stopRedis, observe: async () => {} }), /Separate proposer and approver/);
  }
  assert.equal(stopped, false);
});

test('recovery does not stop Redis when the Console approval is missing', async () => {
  let stopped = false;
  await assert.rejects(exerciseAgentRecovery({
    managerUrl: 'http://127.0.0.1:1', proposerKey: 'proposer', approverKey: 'approver',
    stopRedis: async () => { stopped = true; }, observe: async () => {},
  }), /Console approval is required/);
  assert.equal(stopped, false);
});

test('publication does not call the server when the Console approval is missing', async () => {
  let called = false;
  const original = globalThis.fetch;
  globalThis.fetch = async () => { called = true; return new Response('{}', { status: 500 }); };
  try {
    await assert.rejects(exerciseAgentPublication({
      serverUrl: 'http://127.0.0.1:1', proposerKey: 'proposer', verifierKey: 'verifier',
      expectedActor: 'actor', datasets: [], observe: async () => {},
    }), /Console approval is required/);
  } finally {
    globalThis.fetch = original;
  }
  assert.equal(called, false);
});

test('an API-key approval must be refused and must leave the proposal waiting', async () => {
  const original = globalThis.fetch;
  const calls = [];
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), method: init?.method ?? 'GET' });
    if (String(url).endsWith('/approve')) return new Response('{}', { status: 403 });
    return new Response(JSON.stringify({ status: 'AwaitingApproval' }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  };
  try {
    await refuseApiKeyApproval('https://server.example', 'key', '/api/v1/admin/proposals/p1');
  } finally {
    globalThis.fetch = original;
  }
  assert.deepEqual(calls.map(call => call.method), ['POST', 'GET']);
  assert.equal(calls[0].url, 'https://server.example/api/v1/admin/proposals/p1/approve');
});

test('an API-key approval that the server accepts fails the control', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } });
  try {
    await assert.rejects(
      refuseApiKeyApproval('https://server.example', 'key', '/api/v1/admin/proposals/p1'),
      /HTTP 403/);
  } finally {
    globalThis.fetch = original;
  }
});

test('an API-key refusal that changes the proposal fails the control', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async (url) => {
    if (String(url).endsWith('/approve')) return new Response('{}', { status: 403 });
    return new Response(JSON.stringify({ status: 'Succeeded' }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  };
  try {
    await assert.rejects(
      refuseApiKeyApproval('https://server.example', 'key', '/api/v1/admin/proposals/p1'),
      /changed the proposal/);
  } finally {
    globalThis.fetch = original;
  }
});
