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

const newerItem = {
  ...item,
  itemId: 'inland-river-levels',
  title: 'Inland River Levels',
  description: 'River monitoring data.',
  updatedAt: '2026-08-21T12:00:00Z',
  tags: ['river'],
  distributions: [{ accessUrl: 'https://data.example.org/river-levels.csv', format: 'CSV' }],
};

const publications = [item, newerItem];

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
        const publication = publications.find(entry => path.endsWith('/' + entry.itemId));
        const data = publication ?? { items: publications.map(page => ({ itemType: 'service', page })) };
        await route.fulfill({ json: { success: true, data } });
      });
      if (screen.id === '16-public-service') {
        await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin });
      }
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
        await expect(page.locator('[data-share-opendata-downloads] a')).toHaveAttribute('href', item.distributions[0].accessUrl);
        await expect(page.getByText(item.license, { exact: true })).toBeVisible();
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      await testInfo.attach(`${screen.id}-${width}-${theme}`, {
        body: await page.screenshot({ fullPage: true, animations: 'disabled' }),
        contentType: 'image/png',
      });
      if (screen.id === '16-public-service') {
        const copy = page.getByRole('button', { name: /^Copy:/ });
        await copy.press('Enter');
        await expect(copy).toHaveText('Copied');
        await expect.poll(() => page.evaluate(() => navigator.clipboard.readText()))
          .toBe(item.distributions[0].accessUrl);
      }
      if (screen.id === '15-public-catalog') {
        const search = page.getByRole('searchbox', { name: 'Search open data' });
        const resultLinks = page.locator('.public-catalog-card h3 a');
        await expect(resultLinks).toHaveText([newerItem.title, item.title]);
        await page.getByLabel('Sort').selectOption('title');
        await expect(resultLinks).toHaveText([item.title, newerItem.title]);
        const format = page.getByRole('button', { name: 'CSV', exact: true });
        await format.press('Enter');
        await expect(format).toHaveAttribute('aria-pressed', 'true');
        await expect(resultLinks).toHaveText([newerItem.title]);
        await page.getByRole('button', { name: 'Remove CSV filter' }).press('Enter');
        await expect(resultLinks).toHaveText([item.title, newerItem.title]);
        await search.fill('no matching publication');
        await search.press('Enter');
        await expect(page.getByRole('heading', { name: '0 results', exact: true })).toBeVisible();
        await search.fill('coastal');
        await search.press('Enter');
        await expect(page.getByRole('heading', { name: '1 results', exact: true })).toBeVisible();
        await page.getByRole('button', { name: /^Service.*\(2\)$/ }).click();
        await page.getByRole('button', { name: /^Remove Service.* filter$/ }).click();
        await page.getByRole('link', { name: item.title, exact: true }).press('Enter');
        await expect(page.getByRole('heading', { name: item.title, exact: true })).toBeVisible();
      }
    });
  }
}

test('public catalog explains when the result set is incomplete', async ({ page }) => {
  const requestedCursors: (string | null)[] = [];
  await page.route(`${origin}/api/v1/open-data/datasets?**`, async route => {
    const cursor = new URL(route.request().url()).searchParams.get('cursor');
    requestedCursors.push(cursor);
    const pageNumber = Number(cursor ?? '1');
    await route.fulfill({
      json: {
        success: true,
        data: {
          items: [{ itemType: 'service', page: { ...item, itemId: `publication-${pageNumber}` } }],
          nextCursor: String(pageNumber + 1),
        },
      },
    });
  });

  await page.goto(origin + publicCatalog);

  await expect(page.getByText('Search and filters cover only the items shown here. The server has more public items.'))
    .toBeVisible();
  expect(requestedCursors).toEqual([null, '2', '3', '4']);
});
