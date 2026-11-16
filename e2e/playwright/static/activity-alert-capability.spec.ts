import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5197';
const alertPaths = [
  '/api/v1/admin/observability/alerts',
  '/api/v1/admin/alerts/rules',
  '/api/v1/admin/alerts/zones',
];

for (const advertised of [false, true]) {
  test(`Activity ${advertised ? 'reads' : 'skips'} alert collections when capability is ${advertised ? 'available' : 'absent'}`, async ({ page }, testInfo) => {
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 5197, {
        configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
        handleApi: createContractApi({ allowedOrigin: origin }),
      });
    const requested: string[] = [];
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    try {
      await page.route('**/api/v1/capabilities/manifest', route => route.fulfill({ json: {
        schemaVersion: 'honua.capability_manifest.v1',
        capabilities: advertised ? [{ id: 'alerts.geofence', lifecycle: 'Implemented', optInRequired: false, available: true, supported: true }] : [],
      } }));
      await page.route('**/api/v1/admin/**', route => {
        const path = new URL(route.request().url()).pathname;
        if (path.replace(/\/$/, '') === '/api/v1/admin/services') {
          return route.fulfill({ json: { success: true, data: { items: [{ serviceName: 'fixture-service' }], total: 1 } } });
        }
        if (!alertPaths.includes(path)) return route.continue();
        requested.push(path);
        return route.fulfill({ status: 403, json: { title: 'Forbidden', detail: 'Alert access denied.' } });
      });
      await signInWithFixturePassword(page, origin, '/inbox');
      await page.waitForURL(url => url.pathname === '/console/inbox');
      await page.goto(`${origin}/console/operate/observability`);
      await expect(page.locator('.operate-summary-tile').first()).toBeVisible();
      if (advertised) {
        await expect.poll(() => [...new Set(requested)].sort()).toEqual([...alertPaths].sort());
        await expect(page.getByText('Alert access denied.', { exact: true }).first()).toBeVisible();
      } else {
        await expect(page.getByText('Alerts are not enabled or available on this server.', { exact: false }).first()).toBeVisible();
        expect(requested).toEqual([]);
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(testInfo.project.use.viewport!.width);
      expect(errors).toEqual([]);
      await testInfo.attach('activity-alert-capability', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
    } finally {
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
