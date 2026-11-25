import { expect, test, type Page } from '@playwright/test';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

interface SseClient {
  topic: string;
  res: ServerResponse;
}

interface LiveFixtureState {
  origin: string;
  proposalCount: number;
  healthStatus: string;
  sseClients: SseClient[];
  baseApi: (req: IncomingMessage, res: ServerResponse) => Promise<void>;
}

function writeJson(res: ServerResponse, payload: unknown) {
  res.writeHead(200, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(payload));
}

function buildHealthSnapshot(status: string, description: string) {
  return {
    overallStatus: status,
    generatedAt: '2026-11-24T02:02:00Z',
    health: { status, totalDurationMs: status === 'degraded' ? 42 : 10, entries: [{ name: 'postgres', status, durationMs: status === 'degraded' ? 40 : 4, description }] },
    servingLatency: { windowSeconds: 300, protocols: [] },
    geoprocessing: { available: true, totalActive: status === 'degraded' ? 1 : 0, buckets: [] },
    alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
    deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
    database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.2, cacheHitRatio: 0.95, errorRate: 0, activeConnections: 3, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
    managedTargets: [],
  };
}

function attachSseClient(req: IncomingMessage, res: ServerResponse, state: LiveFixtureState) {
  const url = new URL(req.url ?? '/', state.origin);
  const topic = url.searchParams.get('topics') ?? '';
  res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-store', Connection: 'keep-alive' });
  res.write('event: status\nid: admin:1\ndata: {"state":"connected"}\n\n');
  state.sseClients.push({ topic, res });
  req.on('close', () => {
    const idx = state.sseClients.findIndex(entry => entry.res === res);
    if (idx >= 0) state.sseClients.splice(idx, 1);
  });
}

function respondToLiveFixtureGet(req: IncomingMessage, res: ServerResponse, state: LiveFixtureState) {
  const pathname = new URL(req.url ?? '/', state.origin).pathname.replace(/\/$/, '');
  if (pathname === '/api/v1/admin/live') {
    attachSseClient(req, res, state);
    return true;
  }
  if (pathname === '/api/v1/admin/proposals') {
    const proposals = state.proposalCount > 0 ? [{
      proposalId: 'agent-live-1', kind: 'deploy', status: 'awaiting_approval', riskLevel: 'medium',
      summary: 'Agent live-pushed service rollout', requestedBy: 'mcp-agent', createdAt: '2026-11-24T02:00:00Z',
    }] : [];
    writeJson(res, { proposals });
    return true;
  }
  if (pathname.endsWith('/ops-health')) {
    writeJson(res, buildHealthSnapshot(state.healthStatus, 'OK'));
    return true;
  }
  if (pathname.endsWith('/ops-health/history')) {
    writeJson(res, { from: '2026-11-24T01:00:00Z', to: '2026-11-24T02:00:00Z', resolution: '1m', perReplica: false, latencySeries: [], vitalsPoints: [] });
    return true;
  }
  return false;
}

function pushSseEvent(state: LiveFixtureState, topicFilter: string, frame: string) {
  for (const client of state.sseClients.filter(entry => entry.topic.includes(topicFilter))) {
    client.res.write(frame);
  }
}

async function verifyProposalSseSync(page: Page, state: LiveFixtureState) {
  await signInWithFixturePassword(page, state.origin, '/inbox');
  await page.waitForURL(url => url.pathname === '/console/inbox');
  await expect(page.locator('[data-live-state="live"]')).toBeVisible();
  await expect(page.locator('[data-awaiting-count]')).toContainText('0');

  state.proposalCount = 1;
  const payload = JSON.stringify({
    proposalId: 'agent-live-1', kind: 'deploy', status: 'awaiting_approval',
    requestedBy: 'mcp-agent', riskLevel: 'medium', generatedAt: '2026-11-24T02:01:00Z',
  });
  pushSseEvent(state, 'proposals', `event: ProposalPending\nid: admin:2\ndata: ${payload}\n\n`);
  await expect(page.locator('[data-awaiting-count]')).toContainText('1');
  await expect(page.getByText('Agent live-pushed service rollout')).toBeVisible();
}

async function verifyHealthSseSync(page: Page, state: LiveFixtureState) {
  await page.goto(`${state.origin}/console/operate/health`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect.poll(() => state.sseClients.some(entry => entry.topic.includes('ops-health'))).toBe(true);

  state.healthStatus = 'degraded';
  const payload = JSON.stringify(buildHealthSnapshot('degraded', 'Slow replica'));
  pushSseEvent(state, 'ops-health', `event: OpsHealthSnapshot\nid: admin:2\ndata: ${payload}\n\n`);
  await expect(page.getByText('Slow replica')).toBeVisible();
}

test('agent proposal and health SSE events update the open Console view in place without reload', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'SSE contract test runs once in desktop project');
  const port = 5192;
  const origin = `http://127.0.0.1:${port}`;
  const state: LiveFixtureState = { origin, proposalCount: 0, healthStatus: 'healthy', sseClients: [], baseApi: createContractApi({ allowedOrigin: origin }) };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)),
    port,
    {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (req, res) => {
        if (req.method === 'GET' && respondToLiveFixtureGet(req, res, state)) return;
        await state.baseApi(req, res);
      },
    },
  );

  try {
    await verifyProposalSseSync(page, state);
    await verifyHealthSseSync(page, state);
  } finally {
    for (const client of state.sseClients) client.res.end();
    await new Promise<void>((resolve, reject) => server.close(err => (err ? reject(err) : resolve())));
  }
});
