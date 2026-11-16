import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

test('confirmation keeps keyboard focus modal and restores its trigger', async ({ page }, testInfo) => {
  const mutations: string[] = [];
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (request, response) => {
        response.setHeader('Content-Type', 'application/json');
        if (request.method !== 'GET') mutations.push(`${request.method} ${request.url}`);
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (path.endsWith('/auth/session')) {
          response.end(JSON.stringify({ isAuthenticated: true, claims: [
            { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'keyboard-operator' },
            { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
          ] }));
        } else if (path.includes('/api-keys') && request.method === 'GET') {
          response.end(JSON.stringify({ success: true, data: [{
            id: 'b31be041-5f96-44d8-a1dc-d2bcb05fbf19', name: 'Synthetic audit key',
            keyPrefix: 'fixture', permissions: ['admin:read'], status: 'active', createdAt: '2026-11-01T00:00:00Z',
          }] }));
        } else {
          response.writeHead(404).end('{}');
        }
      },
    });
  try {
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('Console fixture did not bind a TCP port');
    await page.goto(`http://127.0.0.1:${address.port}/console/operate/settings/api-keys`);
    const trigger = page.getByRole('button', { name: 'Revoke Synthetic audit key', exact: true });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('alertdialog');
    const cancel = dialog.getByRole('button', { name: 'Cancel', exact: true });
    const confirm = dialog.locator('[data-console-confirm-accept]');
    await expect(cancel).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(confirm).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(cancel).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(confirm).toBeFocused();
    await testInfo.attach('confirmation-keyboard', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(cancel).toBeFocused();
    await cancel.click();
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(cancel).toBeFocused();
    await dialog.click({ position: { x: 4, y: 4 } });
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    expect(mutations).toEqual([]);
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});
