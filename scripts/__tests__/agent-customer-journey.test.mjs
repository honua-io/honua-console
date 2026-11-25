import { test } from 'node:test';
import assert from 'node:assert/strict';
import { approveRecordedProposal, exerciseAgentPublication, exerciseAgentRecovery, importSummary, refuseApiKeyApproval } from '../agent-customer-journey.mjs';

test('recovery refuses a missing or shared approver before stopping the managed service', async () => {
  let stopped = false;
  const stopService = async () => { stopped = true; };
  for (const approverKey of ['', 'same']) {
    await assert.rejects(exerciseAgentRecovery({ managerUrl: 'http://127.0.0.1:1', proposerKey: 'same', approverKey,
      service: 'postgres', stopService, observe: async () => {} }), /Separate proposer and approver/);
  }
  assert.equal(stopped, false);
});

test('recovery does not stop the managed service when the Console approval is missing', async () => {
  let stopped = false;
  await assert.rejects(exerciseAgentRecovery({
    managerUrl: 'http://127.0.0.1:1', proposerKey: 'proposer', approverKey: 'approver', service: 'postgres',
    stopService: async () => { stopped = true; }, observe: async () => {},
  }), /Console approval is required/);
  assert.equal(stopped, false);
});

test('recovery needs a named managed service before stopping anything', async () => {
  let stopped = false;
  await assert.rejects(exerciseAgentRecovery({
    managerUrl: 'http://127.0.0.1:1', proposerKey: 'proposer', approverKey: 'approver', approve: async () => {},
    stopService: async () => { stopped = true; }, observe: async () => {},
  }), /managed Compose service/);
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

test('a server without approval-review still approves with the API key', async () => {
  const original = globalThis.fetch;
  const calls = [];
  let clicked = false;
  globalThis.fetch = async (url, init) => {
    calls.push({ url: String(url), method: init?.method ?? 'GET' });
    return new Response('{}', { status: String(url).endsWith('/approval-review') ? 404 : 200, headers: { 'Content-Type': 'application/json' } });
  };
  try {
    await approveRecordedProposal({
      base: 'https://server.example', key: 'key', proposalPath: '/api/v1/admin/proposals/p1',
      proposalId: 'p1', consoleProposalId: 'p1', approve: async () => { clicked = true; },
    });
  } finally {
    globalThis.fetch = original;
  }
  assert.equal(clicked, false);
  assert.deepEqual(calls.map(call => [call.method, call.url.endsWith('/approve')]), [['GET', false], ['POST', true]]);
});

test('a server with approval-review refuses the API key and then uses the Console', async () => {
  const original = globalThis.fetch;
  const calls = [];
  let clicked = false;
  globalThis.fetch = async (url, init) => {
    calls.push(init?.method ?? 'GET');
    if (String(url).endsWith('/approve')) return new Response('{}', { status: 403 });
    if (String(url).endsWith('/approval-review')) return new Response('{}', { status: 200 });
    return new Response(JSON.stringify({ status: 'AwaitingApproval' }), { status: 200, headers: { 'Content-Type': 'application/json' } });
  };
  try {
    await approveRecordedProposal({
      base: 'https://server.example', key: 'key', proposalPath: '/api/v1/admin/proposals/p1',
      proposalId: 'p1', consoleProposalId: 'manager:p1', approve: async () => { clicked = true; },
    });
  } finally {
    globalThis.fetch = original;
  }
  assert.equal(clicked, true);
  assert.deepEqual(calls, ['GET', 'POST', 'GET']);
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

test('an unpublishable import names the missing fields and the server warning', () => {
  const summary = JSON.parse(importSummary({ success: true, schema: 'public', table: 't', geometryColumn: 'geometry', primaryKey: 'id',
    warnings: ['No unique registered secure connection could be resolved'], data: 'secret,row' }));
  assert.deepEqual(summary.missing, ['connectionId']);
  assert.equal(summary.success, true);
  assert.match(summary.warnings[0], /No unique registered/);
  assert.equal(JSON.stringify(summary).includes('secret,row'), false);
});

test('publication resolves the configured catalog connection when ingest reports ambiguity', async () => {
  const original = globalThis.fetch;
  const publishReached = new Error('publish reached');
  const connectionId = 'dcb1e5e7-489b-4363-97f0-16be8d93c4ff';
  globalThis.fetch = async (url, init) => {
    if (url.pathname === '/api/v1/admin/connections/') {
      return Response.json({ data: [
        { name: 'alias', connectionId: '34816fae-ccf6-43eb-bbca-3c831531a2c8' },
        { name: 'catalog', connectionId },
      ] });
    }
    assert.equal(url.pathname, '/mcp');
    const { id, method, params } = JSON.parse(init.body);
    if (method === 'initialize') {
      return Response.json({ id, result: {} }, { headers: { 'mcp-session-id': 'test-session' } });
    }
    if (params.name === 'honua_ingest_dataset') {
      return Response.json({ id, result: { structuredContent: {
        success: true, connectionId: null, schema: 'public', table: 'sites', geometryColumn: 'geom', primaryKey: 'id',
      } } });
    }
    assert.equal(params.name, 'honua_publish_service');
    assert.equal(params.arguments.connectionId, connectionId);
    throw publishReached;
  };
  try {
    const journey = {
      serverUrl: 'https://server.example', proposerKey: 'proposer', verifierKey: 'verifier',
      expectedActor: 'actor', approve: async () => {},
      datasets: [{ format: 'geojson', name: 'sites', data: '{}', geometryType: 'Point' }],
    };
    await assert.rejects(exerciseAgentPublication(journey), /did not return a publishable table/);
    await assert.rejects(exerciseAgentPublication({ ...journey, catalogConnectionName: 'missing' }), /does not resolve/);
    await assert.rejects(exerciseAgentPublication({ ...journey, catalogConnectionName: 'catalog' }), publishReached);
  } finally {
    globalThis.fetch = original;
  }
});
