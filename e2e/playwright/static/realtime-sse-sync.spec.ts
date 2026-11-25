import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

test('agent proposal and health SSE events update the open Console view in place without reload', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'SSE contract test runs once in desktop project');
  const port = 5192;
  const origin = `http://127.0.0.1:${port}`;
  let proposalCount = 0;
  let healthStatus = 'healthy';
  const sseClients: Array<{ topic: string; res: import('node:http').ServerResponse }> = [];

  const baseApi = createContractApi({ allowedOrigin: origin });
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)),
    port,
    {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (req, res) => {
        const url = new URL(req.url ?? '/', origin);
        const pathname = url.pathname.replace(/\/$/, '');
        if (pathname === '/api/v1/admin/live' && req.method === 'GET') {
          const topics = url.searchParams.get('topics') ?? '';
          res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-store',
            Connection: 'keep-alive',
          });
          res.write(': connected\n\n');
          sseClients.push({ topic: topics, res });
          req.on('close', () => {
            const idx = sseClients.findIndex(entry => entry.res === res);
            if (idx >= 0) sseClients.splice(idx, 1);
          });
          return;
        }
        if (pathname === '/api/v1/admin/proposals' && req.method === 'GET') {
          const proposals = proposalCount > 0 ? [{
            proposalId: 'agent-live-1',
            kind: 'deploy',
            status: 'awaiting_approval',
            riskLevel: 'medium',
            summary: 'Agent live-pushed service rollout',
            requestedBy: 'mcp-agent',
            createdAt: '2026-11-24T02:00:00Z',
          }] : [];
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ proposals }));
          return;
        }
        if (pathname === '/api/v1/admin/ops/health' && req.method === 'GET') {
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({
            overallStatus: healthStatus,
            generatedAt: '2026-11-24T02:00:00Z',
            health: { status: healthStatus, totalDurationMs: 10, entries: [{ name: 'postgres', status: healthStatus, durationMs: 4, description: 'OK' }] },
            servingLatency: { windowSeconds: 300, protocols: [] },
            geoprocessing: { available: true, totalActive: 0, buckets: [] },
            alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
            deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
            database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.2, cacheHitRatio: 0.95, errorRate: 0, activeConnections: 3, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
            managedTargets: [],
          }));
          return;
        }
        if (pathname === '/api/v1/admin/ops/health/history' && req.method === 'GET') {
          res.writeHead(200, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ from: '2026-11-24T01:00:00Z', to: '2026-11-24T02:00:00Z', resolution: '1m', perReplica: false, latencySeries: [], vitalsPoints: [] }));
          return;
        }
        await baseApi(req, res);
      },
    },
  );

  try {
    await signInWithFixturePassword(page, origin, '/inbox');
    await page.waitForURL(url => url.pathname === '/console/inbox');
    await expect(page.locator('[data-live-state="live"]')).toBeVisible();
    await expect(page.locator('[data-awaiting-count]')).toContainText('0');

    // Push a proposal event from an external agent over SSE without reloading the page.
    proposalCount = 1;
    const payload = JSON.stringify({
      proposalId: 'agent-live-1',
      kind: 'deploy',
      status: 'awaiting_approval',
      requestedBy: 'mcp-agent',
      riskLevel: 'medium',
      generatedAt: '2026-11-24T02:01:00Z',
    });
    for (const client of sseClients.filter(entry => entry.topic.includes('proposals'))) {
      client.res.write(`event: ProposalPending\ndata: ${payload}\n\n`);
    }

    await expect(page.locator('[data-awaiting-count]')).toContainText('1');
    await expect(page.getByText('Agent live-pushed service rollout')).toBeVisible();

    // Navigate to Health and push a degraded OpsHealthSnapshot over SSE to verify in-place card updates.
    await page.goto(`${origin}/console/operate/health`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect.poll(() => sseClients.some(entry => entry.topic.includes('ops-health'))).toBe(true);
    healthStatus = 'degraded';
    const healthEvent = JSON.stringify({
      overallStatus: 'degraded',
      generatedAt: '2026-11-24T02:02:00Z',
      health: { status: 'degraded', totalDurationMs: 42, entries: [{ name: 'postgres', status: 'degraded', durationMs: 40, description: 'Slow replica' }] },
      servingLatency: { windowSeconds: 300, protocols: [] },
      geoprocessing: { available: true, totalActive: 1, buckets: [] },
      alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
      deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
      database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.85, cacheHitRatio: 0.9, errorRate: 0.02, activeConnections: 18, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
      managedTargets: [],
    });
    for (const client of sseClients.filter(entry => entry.topic.includes('ops-health'))) {
      client.res.write(`event: OpsHealthSnapshot\ndata: ${healthEvent}\n\n`);
    }

    await expect(page.getByText('Slow replica')).toBeVisible();
  } finally {
    for (const client of sseClients) client.res.end();
    await new Promise<void>((resolve, reject) => server.close(err => (err ? reject(err) : resolve())));
  }
});
