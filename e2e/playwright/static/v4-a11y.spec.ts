import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

async function mockPopulatedConsoleApis(page: Page) {
  await page.route(`${origin}/api/v1/admin/services**`, route => route.fulfill({
    json: {
      success: true,
      data: {
        items: [{
          name: 'transport-roads',
          folderId: null,
          enabledProtocols: ['FeatureServer', 'OgcFeatures'],
          layerCount: 2,
          allowAnonymous: false,
        }],
        page: 1,
        pageSize: 100,
        total: 1,
        layerTotal: 2,
      },
    },
  }));
  await page.route(`${origin}/api/v1/admin/proposals**`, route => route.fulfill({
    json: {
      proposals: [{
        proposalId: 'prop-a11y-1',
        kind: 'deploy',
        status: 'awaiting_approval',
        riskLevel: 'medium',
        summary: 'Promote vector tile cache settings for roads',
        requestedBy: 'ops-agent',
        createdAt: '2026-11-24T01:00:00Z',
      }],
    },
  }));
  await page.route(`${origin}/api/v1/admin/ops/health`, route => route.fulfill({
    json: {
      overallStatus: 'healthy',
      generatedAt: '2026-11-24T01:05:00Z',
      health: { status: 'healthy', totalDurationMs: 12.4, entries: [{ name: 'postgres', status: 'healthy', durationMs: 4.1, description: 'Connected' }] },
      servingLatency: { windowSeconds: 300, protocols: [{ protocol: 'OgcFeatures', requestCount: 120, errorCount: 0, errorRate: 0, p50Ms: 12, p95Ms: 34, p99Ms: 52, maxMs: 70 }] },
      geoprocessing: { available: true, totalActive: 0, buckets: [] },
      alertDispatch: { dispatcherEnabled: true, dispatcherRunning: true, storagePollFailing: false, pendingCount: 0, deadLetteredCount: 0 },
      deploy: { readyForCoordinatedDeploy: true, pendingMigrationsCount: 0, pendingContractScriptsCount: 0 },
      database: { hasConnectionPoolData: true, connectionPoolUtilization: 0.25, cacheHitRatio: 0.96, errorRate: 0, activeConnections: 4, connectionAcquisitionTimeouts: 0, connectionAcquisitionFailures: 0 },
      managedTargets: [],
    },
  }));
  await page.route(`${origin}/api/v1/admin/ops/health/history**`, route => route.fulfill({
    json: { from: '2026-11-24T00:05:00Z', to: '2026-11-24T01:05:00Z', resolution: '1m', perReplica: false, latencySeries: [], vitalsPoints: [] },
  }));
  await page.route(`${origin}/api/v1/admin/api-keys**`, route => route.fulfill({
    json: {
      success: true,
      data: [{
        id: 'key-1',
        name: 'Field Collector',
        keyPrefix: 'hn_live_abc',
        permissions: ['features:read'],
        status: 'active',
        expiresAt: null,
        revokedAt: null,
      }],
    },
  }));
  await page.route(`${origin}/api/v1/open-data/datasets**`, route => route.fulfill({
    json: {
      success: true,
      data: {
        items: [{
          itemType: 'service',
          page: {
            itemId: 'roads-public',
            title: 'Public Roads',
            description: 'Authoritative public road centerlines.',
            publisherName: 'Transport GIS',
            license: 'CC BY 4.0',
            updatedAt: '2026-11-20T00:00:00Z',
            tags: ['transport'],
            distributions: [{ accessUrl: 'https://data.example.org/roads', format: 'OGC:API:Features' }],
          },
        }],
      },
    },
  }));
}

const primaryRoutes = [
  { name: 'Home', path: '/' },
  { name: 'Services', path: '/operate/services' },
  { name: 'Connections', path: '/operate/connections' },
  { name: 'Approvals', path: '/inbox' },
  { name: 'Health', path: '/operate/health' },
  { name: 'API keys', path: '/operate/settings/api-keys' },
  { name: 'Discovery endpoints', path: '/operate/catalogs' },
];

test('shipping WASM primary v4 routes have zero critical or serious axe violations in light and dark themes', async ({ page }) => {
  await mockPopulatedConsoleApis(page);
  await signInWithFixturePassword(page, origin, '/');
  await page.waitForURL(url => url.pathname === '/console/');

  for (const theme of ['light', 'dark'] as const) {
    if (theme === 'dark') {
      await page.locator('[data-console-theme-toggle]').click();
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    }
    for (const route of primaryRoutes) {
      await page.goto(`${origin}/console${route.path}`);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
      const results = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
        .analyze();
      const severe = results.violations.filter(v => v.impact === 'critical' || v.impact === 'serious');
      expect(severe, `axe violations on ${route.name} (${route.path}) [${theme}]`).toEqual([]);
    }
  }
});

test('public open-data catalog has zero critical or serious axe violations', async ({ page }) => {
  await page.route(`${origin}/api/v1/capabilities/manifest`, route => route.fulfill({
    json: {
      schemaVersion: 'honua.capability_manifest.v1',
      capabilities: [{ id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false, available: true, supported: true }],
    },
  }));
  await mockPopulatedConsoleApis(page);
  await page.goto(`${origin}/console/share/public`);
  await expect(page.getByRole('heading', { name: 'Explore open data', exact: true })).toBeVisible();
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .analyze();
  const severe = results.violations.filter(v => v.impact === 'critical' || v.impact === 'serious');
  expect(severe, 'axe violations on /share/public').toEqual([]);
});
