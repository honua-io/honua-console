import { expect, test, type Page } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

async function checkContainment(page: Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth))
    .toBeLessThanOrEqual(page.viewportSize()!.width);
  expect(await page.locator('#console-main').evaluate(main =>
    [...main.querySelectorAll('input, select, textarea, button')]
      .filter(element => !element.closest('.console-table-wrap'))
      .filter(element => {
        const bounds = element.getBoundingClientRect();
        return bounds.left < -1 || bounds.right > innerWidth + 1;
      }).map(element => element.outerHTML)), 'form controls fit the viewport').toEqual([]);
}

async function checkTableKeyboardAccess(page: Page) {
  const region = page.getByRole('region', { name: 'API keys', exact: true });
  await region.evaluate(element => { element.scrollLeft = 0; });
  await region.focus();
  await page.keyboard.press('Tab');
  const revoke = page.getByRole('button', { name: 'Revoke Synthetic integration key', exact: true });
  await expect(revoke).toBeFocused();
  await expect.poll(() => revoke.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    const regionBounds = element.closest('.console-table-wrap')!.getBoundingClientRect();
    return bounds.left >= regionBounds.left - 1 && bounds.right <= regionBounds.right + 1;
  }), 'keyboard focus reveals the table action').toBe(true);
}

for (const v4Enabled of [false, true]) {
  test(`Settings and API keys reflow without hiding controls (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const mutations: string[] = [];
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
        configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
        handleApi: async (request, response) => {
          response.setHeader('Content-Type', 'application/json');
          if (request.method !== 'GET') mutations.push(`${request.method} ${request.url}`);
          const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
          if (path.endsWith('/auth/session')) {
            response.end(JSON.stringify({ isAuthenticated: true, claims: [
              { type: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier', value: 'fixture-operator' },
              { type: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role', value: 'admin' },
            ] }));
          } else if (path.endsWith('/auth/csrf')) {
            response.end(JSON.stringify({ requestToken: 'fixture-only' }));
          } else {
            const data = path.endsWith('/api-keys') ? [{
              id: '4e386945-c5f5-45bf-8d4a-2fed5a3d7b62', name: 'Synthetic integration key',
              keyPrefix: 'fixture', permissions: ['admin:read'], status: 'active',
              createdAt: '2026-11-01T00:00:00Z', updatedAt: '2026-11-01T00:00:00Z',
            }] : path.endsWith('/version') ? { version: 'fixture', metadataApiVersion: '2' }
              : path.endsWith('/capabilities') ? { metadataSchemaVersion: '2' }
                : path.endsWith('/license') ? { edition: 'Fixture', isValid: true, entitlements: [] }
                  : path.endsWith('/oidc/providers') ? [] : null;
            if (data !== null) response.end(JSON.stringify({ success: true, data }));
            else response.writeHead(404).end('{}');
          }
        },
      });
    try {
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      for (const route of ['settings', 'settings/api-keys']) {
        await page.goto(`http://127.0.0.1:${address.port}/console/operate/${route}`);
        await expect(page.getByRole('heading', { name: route.endsWith('api-keys') ? 'API keys' : 'Settings', exact: true })).toBeVisible();
        if (route.endsWith('api-keys')) {
          if (v4Enabled) await expect(page.locator('#api-key-name')).toBeVisible();
          else await expect(page.locator('.console-state')).toContainText('Unsupported');
        }
        for (const mode of ['normal', 'spacing', 'enlarged', 'combined']) {
          const style = await page.addStyleTag({ content: `
            html { font-size: ${mode === 'enlarged' || mode === 'combined' ? 32 : 16}px !important; }
            ${mode === 'spacing' || mode === 'combined' ? '* { line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important; } p { margin-bottom: 2em !important; }' : ''}
          ` });
          await checkContainment(page);
          if (route.endsWith('api-keys') && v4Enabled) await checkTableKeyboardAccess(page);
          await testInfo.attach(`${route.replaceAll('/', '-')}-${mode}-v4-${v4Enabled}`, {
            body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
          });
          await style.evaluate(element => element.remove());
        }
      }
      expect(mutations, 'read-only reflow checks do not create or revoke keys').toEqual([]);
    } finally {
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
