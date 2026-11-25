import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { mockPopulatedConsoleApis } from './v4-populated-fixture';

const origin = 'http://127.0.0.1:5185';
const widths = [390, 768, 1024, 1280, 1440];

const overflowFixtures = {
  proposal: {
    proposalId: 'prop-long-identifier-20261124-roads-tile-cache-rebalance',
    riskLevel: 'high',
    summary: 'Rebalance vector tile cache and promote regional parcel serving service',
    requestedBy: 'honua-ops-agent',
    createdAt: '2026-11-24T00:15:00Z',
  },
  service: {
    name: 'regional-transport-infrastructure-roads-v2',
    enabledProtocols: ['FeatureServer', 'OgcFeatures', 'Wms', 'OGC-API-Tiles'],
    layerCount: 8,
    allowAnonymous: true,
  },
  opsHealth: {
    health: { status: 'healthy', totalDurationMs: 18.2, entries: [{ name: 'postgres', status: 'healthy', durationMs: 6.1, description: 'Healthy' }] },
    servingLatency: { windowSeconds: 300, protocols: [{ protocol: 'FeatureServer', requestCount: 840, errorCount: 1, errorRate: 0.001, p50Ms: 14, p95Ms: 42, p99Ms: 91, maxMs: 120 }] },
    geoprocessing: { available: true, totalActive: 2, buckets: [{ status: 'running', backend: 'native', count: 2 }] },
    database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.42, cacheHitRatio: 0.98, errorRate: 0, activeConnections: 12, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
  },
  exports: [{
    exportId: 'exp-parcels-2026',
    name: 'Countywide Parcels GeoParquet Export',
    sourceName: 'parcels',
    currentVersion: 3,
    access: 'authenticated',
    publicUrl: 'https://cdn.example.org/exports/countywide-parcels-2026.parquet',
    updatedAt: '2026-11-24T00:30:00Z',
  }],
};

const routes = [
  '/inbox',
  '/operate/services',
  '/operate/layers',
  '/operate/health',
  '/operate/exports',
];

test('populated primary v4 routes have zero horizontal overflow across 390–1440px viewports', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'Matrix explicitly tests all widths from 390 to 1440 in desktop project');
  await mockPopulatedConsoleApis(page, origin, overflowFixtures);
  await signInWithFixturePassword(page, origin, '/inbox');
  await page.waitForURL(url => url.pathname === '/console/inbox');

  for (const route of routes) {
    await page.goto(`${origin}/console${route}`);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    for (const width of widths) {
      await page.setViewportSize({ width, height: 900 });
      const metrics = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        innerWidth: window.innerWidth,
      }));
      expect(metrics.scrollWidth, `overflow on ${route} at ${width}px`).toBeLessThanOrEqual(metrics.innerWidth + 1);
    }
  }
});
