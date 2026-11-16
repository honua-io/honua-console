import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

for (const v4Enabled of [false, true]) {
  test(`support draft stays locked during context capture (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    let releaseCapture: () => void = () => {};
    const captureReleased = new Promise<void>(resolve => { releaseCapture = resolve; });
    let captures = 0;
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
        configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
        handleApi: async (request, response) => {
          response.setHeader('Content-Type', 'application/json');
          const path = new URL(request.url!, 'http://localhost').pathname;
          if (path.endsWith('/auth/session')) {
            response.end(JSON.stringify({ isAuthenticated: true, claims: [
              { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
              { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
            ] }));
          } else if (path.endsWith('/observability/errors')) {
            if (++captures > 1) await captureReleased;
            response.end(JSON.stringify({ errors: [] }));
          } else {
            response.writeHead(404).end('{}');
          }
        },
      });
    try {
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await page.goto(`http://127.0.0.1:${address.port}/console/support`);
      await expect(page.locator('.support-context-list')).toBeVisible();
      await page.getByLabel('Requested action', { exact: true }).fill('Diagnose synthetic errors');
      await page.getByLabel('Symptoms', { exact: true }).fill('Synthetic failure');
      await page.getByRole('button', { name: 'Submit ticket', exact: true }).click();
      await expect.poll(() => captures).toBe(2);
      for (const label of ['Severity', 'Service', 'Requested action', 'Allowed access mode',
        'Access TTL (minutes)', 'Scoped telemetry key (optional)', 'Symptoms']) {
        await expect(page.getByLabel(label, { exact: true })).toBeDisabled();
      }
      await expect(page.locator('#support-rollback')).toBeDisabled();
      await testInfo.attach(`pending-support-v4-${v4Enabled}`, {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
      releaseCapture();
      await expect(page.getByRole('alert')).toContainText('In-product support is not configured');
      await expect(page.getByLabel('Requested action', { exact: true })).toBeEnabled();
      await expect(page.getByLabel('Requested action', { exact: true })).toHaveValue('Diagnose synthetic errors');
      await expect(page.getByLabel('Symptoms', { exact: true })).toHaveValue('Synthetic failure');
    } finally {
      releaseCapture();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
