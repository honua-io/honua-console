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

function writeJson(res: ServerResponse, payload: unknown) {
  res.writeHead(200, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(payload));
}

function buildHealthSnapshot(status: string, description: string) {
  const degraded = status === 'degraded';
  return {
    overallStatus: status,
    generatedAt: '2026-11-24T02:02:00Z',
    health: { status, totalDurationMs: degraded ? 42 : 10, entries: [{ name: 'postgres', status, durationMs: degraded ? 40 : 4, description }] },
    servingLatency: { windowSeconds: 300, protocols: [] },
    geoprocessing: { available: true, totalActive: degraded ? 1 : 0, buckets: [] },
    alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
    deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
    database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.2, cacheHitRatio: 0.95, errorRate: 0, activeConnections: 3, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
    managedTargets: [],
  };
}

class LiveSseFixture {
  readonly origin: string;
  private proposalCount = 0;
  private healthStatus = 'healthy';
  private readonly sseClients: SseClient[] = [];
  private readonly baseApi: (req: IncomingMessage, res: ServerResponse) => Promise<void>;

  constructor(origin: string) {
    this.origin = origin;
    this.baseApi = createContractApi({ allowedOrigin: origin });
  }

  hasTopicSubscriber(topic: string) {
    return this.sseClients.some(entry => entry.topic.includes(topic));
  }

  publishPendingProposal() {
    this.proposalCount = 1;
    const payload = JSON.stringify({
      proposalId: 'agent-live-1', kind: 'deploy', status: 'awaiting_approval',
      requestedBy: 'mcp-agent', riskLevel: 'medium', generatedAt: '2026-11-24T02:01:00Z',
    });
    this.broadcast('proposals', `event: ProposalPending\nid: admin:2\ndata: ${payload}\n\n`);
  }

  publishDegradedHealth() {
    this.healthStatus = 'degraded';
    const payload = JSON.stringify(buildHealthSnapshot('degraded', 'Slow replica'));
    this.broadcast('ops-health', `event: OpsHealthSnapshot\nid: admin:2\ndata: ${payload}\n\n`);
  }

  closeStreams() {
    for (const client of this.sseClients) client.res.end();
  }

  async handleApi(req: IncomingMessage, res: ServerResponse) {
    if (req.method === 'GET' && this.tryHandleGet(req, res)) return;
    await this.baseApi(req, res);
  }

  private tryHandleGet(req: IncomingMessage, res: ServerResponse) {
    const pathname = new URL(req.url ?? '/', this.origin).pathname.replace(/\/$/, '');
    if (pathname === '/api/v1/admin/live') {
      this.attachSseClient(req, res);
      return true;
    }
    if (pathname === '/api/v1/admin/proposals') {
      const proposals = this.proposalCount > 0 ? [{
        proposalId: 'agent-live-1', kind: 'deploy', status: 'awaiting_approval', riskLevel: 'medium',
        summary: 'Agent live-pushed service rollout', requestedBy: 'mcp-agent', createdAt: '2026-11-24T02:00:00Z',
      }] : [];
      writeJson(res, { proposals });
      return true;
    }
    if (pathname.endsWith('/ops-health')) {
      writeJson(res, buildHealthSnapshot(this.healthStatus, 'OK'));
      return true;
    }
    if (pathname.endsWith('/ops-health/history')) {
      writeJson(res, { from: '2026-11-24T01:00:00Z', to: '2026-11-24T02:00:00Z', resolution: '1m', perReplica: false, latencySeries: [], vitalsPoints: [] });
      return true;
    }
    return false;
  }

  private attachSseClient(req: IncomingMessage, res: ServerResponse) {
    const topic = new URL(req.url ?? '/', this.origin).searchParams.get('topics') ?? '';
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-store', Connection: 'keep-alive' });
    res.write('event: status\nid: admin:1\ndata: {"state":"connected"}\n\n');
    this.sseClients.push({ topic, res });
    req.on('close', () => {
      const idx = this.sseClients.findIndex(entry => entry.res === res);
      if (idx >= 0) this.sseClients.splice(idx, 1);
    });
  }

  private broadcast(topicFilter: string, frame: string) {
    for (const client of this.sseClients.filter(entry => entry.topic.includes(topicFilter))) {
      client.res.write(frame);
    }
  }
}

async function verifyProposalSseSync(page: Page, fixture: LiveSseFixture) {
  await signInWithFixturePassword(page, fixture.origin, '/inbox');
  await page.waitForURL(url => url.pathname === '/console/inbox');
  await expect(page.locator('[data-live-state="live"]')).toBeVisible();
  await expect(page.locator('[data-awaiting-count]')).toContainText('0');

  fixture.publishPendingProposal();
  await expect(page.locator('[data-awaiting-count]')).toContainText('1');
  await expect(page.getByText('Agent live-pushed service rollout')).toBeVisible();
}

async function verifyHealthSseSync(page: Page, fixture: LiveSseFixture) {
  await page.goto(`${fixture.origin}/console/operate/health`);
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect.poll(() => fixture.hasTopicSubscriber('ops-health')).toBe(true);

  fixture.publishDegradedHealth();
  await expect(page.getByText('Slow replica')).toBeVisible();
}

test('agent proposal and health SSE events update the open Console view in place without reload', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'SSE contract test runs once in desktop project');
  const port = 5192;
  const fixture = new LiveSseFixture(`http://127.0.0.1:${port}`);
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)),
    port,
    {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: (req, res) => fixture.handleApi(req, res),
    },
  );

  try {
    await verifyProposalSseSync(page, fixture);
    await verifyHealthSseSync(page, fixture);
  } finally {
    fixture.closeStreams();
    await new Promise<void>((resolve, reject) => server.close(err => (err ? reject(err) : resolve())));
  }
});
