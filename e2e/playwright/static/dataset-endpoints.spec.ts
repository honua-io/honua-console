import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const connection = '11111111-1111-1111-1111-111111111111';
const datasetId = `${connection}-layer-7`;

for (const v4Enabled of [false, true]) {
  test(`dataset filtering and endpoint selection survive vocabulary changes, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/console/config.json', async route => {
      const response = await route.fetch();
      await route.fulfill({ response, json: { ...await response.json(), v4Enabled } });
    });
    await page.route('**/api/v1/admin/**', async route => {
      const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
      let data: unknown;
      if (path === '/api/v1/admin/services') {
        data = { items: [{ serviceName: 'cadastre', layerCount: 1, enabledProtocols: ['FeatureServer'] }], total: 1, page: 1, pageSize: 100 };
      } else if (path === `/api/v1/admin/connections/${connection}/layers`) {
        data = { items: [{ layerId: 7, layerName: 'Parcels', tableName: 'parcels',
          serviceName: 'cadastre', geometryType: 'Polygon', enabled: true }], total: 1, page: 1, pageSize: 100 };
      } else {
        await route.continue();
        return;
      }
      await route.fulfill({ json: { success: true, data } });
    });
    await signInWithFixturePassword(page, 'http://127.0.0.1:5185', '/console/operate/data');
    await page.goto('http://127.0.0.1:5185/console/operate/data');
    const row = page.locator(`[data-resource-node="${datasetId}"]`);
    await expect(row).toContainText('Parcels');
    const filter = v4Enabled ? page.getByRole('searchbox', { name: 'Search layers' })
      : page.getByRole('searchbox', { name: 'Filter datasets' });
    await filter.fill('absent-dataset');
    await expect(row).toHaveCount(0);
    await page.getByRole('button', { name: v4Enabled ? 'Clear filters' : 'Clear filter', exact: true }).click();
    await expect(row).toBeVisible();
    if (v4Enabled) {
      await row.getByRole('button', { name: 'Map Parcels', exact: true }).click();
    } else {
      await row.getByRole('button', { name: 'Parcels', exact: true }).click();
      await row.locator('[data-publication]').first().getByRole('button').click();
    }
    const preview = page.getByRole('complementary', { name: 'Layer preview', exact: true });
    await expect(preview.getByRole('heading')).toContainText('Parcels');
    await expect(preview.locator('[data-preview-route]')).toContainText('/cadastre/');
    await expect(preview.getByRole('link', { name: 'Open route', exact: true })).toHaveAttribute('href', /cadastre/);
    expect(errors).toEqual([]);
    await testInfo.attach('dataset-endpoint-selection', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  });
}
