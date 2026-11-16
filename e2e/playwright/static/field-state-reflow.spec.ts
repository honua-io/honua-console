import { test, expect } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

// Generated form IDs are unbroken 32-character strings. A title-like ID hides the overflow.
const formId = '0123456789abcdef0123456789abcdef';
const summary = {
  formId, title: 'Field reflow fixture', serviceId: 'unavailable-fixture', layerId: 0,
  currentDraftVersion: 1, updatedAt: '2026-11-16T17:00:00Z',
};
const version = {
  formId, version: 1, status: 'draft', etag: '"fixture-1"', createdAt: summary.updatedAt,
  package: {
    schemaVersion: 'honua.form-package.v1', formId, title: summary.title,
    target: { serviceId: summary.serviceId, layerId: 0 }, sections: [], fields: [],
  },
};

for (const v4Enabled of [false, true]) {
  test(`field rows preserve full generated IDs without overflow, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    let api: ReturnType<typeof createContractApi>;
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)),
      0,
      {
        configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
        handleApi: async (request, response) => {
          const path = new URL(request.url!, 'http://localhost').pathname;
          if (request.method === 'GET' &&
              (path === '/api/v1/admin/forms' || path === `/api/v1/admin/forms/${formId}`)) {
            response.writeHead(200, { 'Content-Type': 'application/json' });
            response.end(JSON.stringify(path.endsWith(formId) ? version : [summary]));
            return;
          }
          await api(request, response);
        },
      },
    );
    const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
    api = createContractApi({ allowedOrigin: origin });
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    try {
      await signInWithFixturePassword(page, origin, '/console/studio/form');
      await page.getByRole('button', { name: summary.title, exact: true }).click();
      await expect(page.getByRole('button', { name: 'Save draft', exact: true })).toBeVisible();
      const id = page.locator('.console-field-state__readonly', { hasText: formId });
      await expect(id).toHaveText(formId);
      await expect(id).toBeVisible();
      const bounds = await id.evaluate(element => {
        const row = element.closest('.console-field-state')!;
        const rect = element.getBoundingClientRect();
        const label = row.querySelector('.console-field-state__label')!.getBoundingClientRect();
        const meta = row.querySelector('.console-field-state__meta')!.getBoundingClientRect();
        return {
          viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
          idLeft: rect.left, idRight: rect.right,
          labelBottom: label.bottom, valueTop: rect.top, valueBottom: rect.bottom, metaTop: meta.top,
        };
      });
      await testInfo.attach('field-row-bounds', { body: JSON.stringify(bounds, null, 2), contentType: 'application/json' });
      await testInfo.attach('field-row-reflow', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
      expect(bounds.rootWidth).toBeLessThanOrEqual(bounds.viewport);
      expect(bounds.idLeft).toBeGreaterThanOrEqual(0);
      expect(bounds.idRight).toBeLessThanOrEqual(bounds.viewport);
      if (bounds.viewport <= 720) {
        expect(bounds.valueTop).toBeGreaterThanOrEqual(bounds.labelBottom);
        expect(bounds.metaTop).toBeGreaterThanOrEqual(bounds.valueBottom);
      }
      expect(pageErrors).toEqual([]);
    } finally {
      await page.close();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
