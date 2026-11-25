import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { mockPopulatedConsoleApis } from './v4-populated-fixture';

const origin = 'http://127.0.0.1:5185';

const a11yFixtures = {
  apiKeys: [{
    id: '22222222-2222-4222-8222-222222222222',
    name: 'Field Collector',
    keyPrefix: 'hn_live_abc',
    permissions: ['features:read'],
    status: 'active',
    createdAt: '2026-11-24T01:00:00Z',
    updatedAt: '2026-11-24T01:00:00Z',
    expiresAt: null,
    revokedAt: null,
  }],
  openDataItems: [{
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
};

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
  await mockPopulatedConsoleApis(page, origin, a11yFixtures);
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
  await mockPopulatedConsoleApis(page, origin, a11yFixtures);
  await page.goto(`${origin}/console/share/public`);
  await expect(page.getByRole('heading', { name: 'Explore open data', exact: true })).toBeVisible();
  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa'])
    .analyze();
  const severe = results.violations.filter(v => v.impact === 'critical' || v.impact === 'serious');
  expect(severe, 'axe violations on /share/public').toEqual([]);
});
