import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const connection = '11111111-1111-1111-1111-111111111111';

for (const v4Enabled of [false, true]) {
  test(`layer map retains its binding through controls with v4=${v4Enabled}`, async ({ page }, testInfo) => {
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
        data = { items: [{ serviceName: 'cadastre', layerCount: 1 }], total: 1, page: 1, pageSize: 100 };
      } else if (path === `/api/v1/admin/connections/${connection}/layers`) {
        data = { items: [{ layerId: 7, layerName: 'Parcels', tableName: 'parcels',
          serviceName: 'cadastre', geometryType: 'Polygon', enabled: true,
          extent: { minX: -1, minY: -1, maxX: 1, maxY: 1 } }], total: 1, page: 1, pageSize: 100 };
      } else if (path === '/api/v1/admin/metadata/layers/7/discovery') {
        data = { etag: 'browser-fixture', entityId: 'parcels', affectedPublications: [
          { publicationId: 'parcels', serviceName: 'cadastre', identifier: '7', previewStorageLayerId: 7 },
        ] };
      } else {
        await route.continue();
        return;
      }
      await route.fulfill({ json: { success: true, data } });
    });
    let styleRequests = 0;
    await page.route('**/api/styles/parcels.json', async route => {
      styleRequests++;
      await route.fulfill({ json: { version: 8, sources: {}, layers: [
        { id: 'background', type: 'background', paint: { 'background-color': '#e7eddf' } },
      ] } });
    });
    await signInWithFixturePassword(page, 'http://127.0.0.1:5185', '/operate/connections');
    await page.goto(`http://127.0.0.1:5185/console/operate/layers/${connection}-layer-7`);
    const map = page.getByRole('figure', { name: 'Map preview of layer Parcels', exact: true });
    await expect(map).toHaveAttribute('data-map-bound', 'true');
    await expect(map.locator('canvas.maplibregl-canvas')).toHaveCount(1);
    await map.getByRole('group', { name: 'Zoom controls', exact: true }).getByRole('button', { name: 'Zoom in', exact: true }).click();
    await map.getByRole('group', { name: 'Zoom controls', exact: true }).getByRole('button', { name: 'Zoom out', exact: true }).click();
    await expect(map).toHaveAttribute('data-map-bound', 'true');
    await expect(map.locator('canvas.maplibregl-canvas')).toHaveCount(1);
    expect(styleRequests).toBe(1);
    await page.goto('http://127.0.0.1:5185/console/operate/connections');
    await expect(map).toHaveCount(0);
    await page.goto(`http://127.0.0.1:5185/console/operate/layers/${connection}-layer-7`);
    await expect(map).toHaveAttribute('data-map-bound', 'true');
    await expect(map.locator('canvas.maplibregl-canvas')).toHaveCount(1);
    expect(errors).toEqual([]);
    await testInfo.attach(`layer-map-v4-${v4Enabled}`, {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  });
}
