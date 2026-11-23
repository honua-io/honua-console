import { expect, test } from '@playwright/test';

const origin = 'http://127.0.0.1:5185';
const catalog = '/console/catalogs';
const publicCatalog = '/console/share/public';
const item = {
  itemId: 'coastal-flood-service',
  title: 'Coastal Flood Service',
  description: 'Coastal flood hazard data for planning and emergency response.',
  publisherName: 'Resilience team',
  license: 'CC BY 4.0',
  updatedAt: '2026-08-20T12:00:00Z',
  tags: ['coastal', 'flood'],
  distributions: [{
    accessUrl: 'https://data.example.org/ogc/features/collections/coastal-flood-hazard-zones',
    format: 'OGC:API:Features',
  }],
};

const screens = [
  { id: '10-catalogs', path: catalog },
  { id: '11-catalog-overview', path: catalog + '/coastal' },
  { id: '12-catalog-settings', path: catalog + '/coastal/settings' },
  { id: '13-catalog-item', path: catalog + '/coastal/items/flood' },
  { id: '14-catalog-categories', path: catalog + '/coastal/categories' },
  { id: '15-public-catalog', path: publicCatalog, heading: 'Explore open data' },
  { id: '16-public-service', path: publicCatalog + '/items/' + item.itemId, heading: item.title },
  { id: '17-catalog-templates', path: catalog + '/coastal/templates' },
  { id: '18-catalog-template-editor', path: catalog + '/coastal/templates/flood' },
];

for (const screen of screens) {
  for (const theme of ['light', 'dark'] as const) {
    test(`${screen.id} renders in ${theme}`, async ({ page, context }, testInfo) => {
      const width = testInfo.project.name === 'mobile' ? 390 : 1440;
      if (!screen.heading) {
        await context.addCookies([{ name: 'console-fixture-session', value: 'issued', url: origin }]);
      }
      await page.route(`${origin}/api/v1/capabilities/manifest`, route => route.fulfill({
        json: {
          schemaVersion: 'honua.capability_manifest.v1',
          capabilities: [{
            id: 'console.content.persistent',
            lifecycle: 'Implemented',
            optInRequired: false,
            available: true,
            supported: true,
          }],
        },
      }));
      await page.route(`${origin}/api/v1/open-data/datasets**`, async route => {
        const path = new URL(route.request().url()).pathname;
        const data = path.endsWith('/' + item.itemId)
          ? item
          : { items: [{ itemType: 'service', page: item }] };
        await route.fulfill({ json: { success: true, data } });
      });
      await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
      await page.goto(origin + screen.path);
      await expect(page.getByRole('heading', {
        name: screen.heading ?? 'Catalogs are not enabled',
        exact: true,
      })).toBeVisible();
      await expect(page.locator('html')).toHaveAttribute('data-console-theme-enabled', 'true');
      if (theme === 'dark') {
        await page.locator('[data-console-theme-toggle]').click();
      }
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
      if (screen.id === '15-public-catalog') {
        await expect(page.getByRole('link', { name: item.title, exact: true })).toBeVisible();
      }
      if (screen.id === '16-public-service') {
        await expect(page.locator('[data-share-opendata-routes] code')).toHaveText(item.distributions[0].accessUrl);
        await expect(page.getByRole('button', { name: /^Copy:/ })).toBeVisible();
        await expect(page.getByText(item.license, { exact: true })).toBeVisible();
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await testInfo.attach(`${screen.id}-${width}-${theme}`, {
        body: await page.screenshot({ fullPage: true, animations: 'disabled' }),
        contentType: 'image/png',
      });
    });
  }
}
