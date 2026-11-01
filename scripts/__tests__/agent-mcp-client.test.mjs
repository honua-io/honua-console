import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { AgentMcpClient } from '../agent-mcp-client.mjs';

test('uses a negotiated MCP session and unwraps structured tool results', async t => {
  const calls = [];
  const server = http.createServer(async (req, res) => {
    let raw = ''; for await (const part of req) raw += part;
    const body = JSON.parse(raw); calls.push({ body, session: req.headers['mcp-session-id'] });
    res.setHeader('content-type', 'application/json');
    if (body.method === 'initialize') res.setHeader('mcp-session-id', 'session-1');
    res.end(JSON.stringify({ jsonrpc: '2.0', id: body.id, result: body.method === 'initialize' ? {} : { structuredContent: { status: 'ok' } } }));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => server.close());
  const client = new AgentMcpClient(`http://127.0.0.1:${server.address().port}`, 'test-key');
  await client.initialize();
  assert.deepEqual(await client.call('honua_ops_findings', { rule: 'test' }), { status: 'ok' });
  assert.equal(calls[1].session, 'session-1');
  assert.equal(calls[1].body.method, 'tools/call');
});

test('rejects tool errors and missing structured output', async t => {
  const server = http.createServer(async (req, res) => {
    let raw = ''; for await (const part of req) raw += part;
    const body = JSON.parse(raw); res.setHeader('content-type', 'application/json');
    if (body.method === 'initialize') res.setHeader('mcp-session-id', 'session-1');
    res.end(JSON.stringify({ jsonrpc: '2.0', id: body.id, result: body.method === 'initialize' ? {} : { isError: true } }));
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => server.close());
  const client = new AgentMcpClient(`http://127.0.0.1:${server.address().port}`, 'test-key');
  await client.initialize();
  await assert.rejects(client.call('honua_ops_findings', {}), /MCP tool/);
});
