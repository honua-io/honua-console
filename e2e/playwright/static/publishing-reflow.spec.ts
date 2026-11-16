import { test, expect } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

const serviceName = 'regional-transportation-network-publication';
const title = 'Regional transportation network and public infrastructure survey';

for (const v4Enabled of [false, true]) {
  test(`publishing destinations reflow with enlarged text, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0,
      { configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
        handleApi: (...args) => api(...args) },
    );
    const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
    const api = createContractApi({ allowedOrigin: origin });
    const errors: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    const measurements: unknown[] = [];
    try {
      await page.route('**/api/v1/admin/**', route => {
        const path = new URL(route.request().url()).pathname.replace(/\/$/, '');
        if (path === '/api/v1/admin/connections') return route.fulfill({ json: { success: true, data: [] } });
        if (path === '/api/v1/admin/services') return route.fulfill({ json: { success: true, data: {
          items: [{ serviceName, title, layerCount: 0 }], page: 1, pageSize: 100, total: 1, layerTotal: 0,
        } } });
        if (path === `/api/v1/admin/services/${serviceName}/settings`) {
          return route.fulfill({ json: { success: true, data: { serviceName } } });
        }
        return route.continue();
      });
      await signInWithFixturePassword(page, origin, '/console/operate/publishing');
      const row = page.locator('[data-publish-tree-row]');
      await expect(row).toHaveCount(1);
      await expect(row.locator('.publish-tree-name')).toHaveText(title);
      await expect(row.locator('.publish-tree-display-name')).toContainText(serviceName);
      await page.addStyleTag({ content: `html[data-test-spacing] * {
        line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important;
      } html[data-test-spacing] p { margin-bottom: 2em !important; }` });
      for (const enlarged of [false, true]) {
        for (const spacing of [false, true]) {
          await page.evaluate(({ enlarged, spacing }) => {
            document.documentElement.style.fontSize = enlarged ? '200%' : '';
            document.documentElement.toggleAttribute('data-test-spacing', spacing);
          }, { enlarged, spacing });
          for (const destination of ['existing', 'new']) {
            await page.getByRole('button', { name: destination === 'new' ? 'Create new service' : 'Use existing service', exact: true }).click();
            if (destination === 'new') {
              const folder = page.getByRole('combobox', { name: 'Publishing Folder', exact: true });
              await expect(folder).toBeEnabled();
              await folder.selectOption('root:');
              const review = page.locator('[data-publish-folder-review]');
              await expect(review).toBeEnabled();
              await page.locator('[data-publish-folder-refresh]').click();
              await expect(review).toBeEnabled();
              await review.click();
              await expect(folder).toHaveValue('root:');
            }
            const bounds = await page.locator('[data-quick-step="service"]').evaluate(section => ({
              viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
              elements: [...section.querySelectorAll('.publish-segment, .publish-segment-option, .publish-tree-row, .publish-tree-name, .publish-tree-display-name, [data-publish-folders] button')]
                .map(element => { const rect = element.getBoundingClientRect(); return {
                  text: element.textContent, left: rect.left, right: rect.right, width: rect.width,
                  scrollWidth: element.scrollWidth, clientWidth: element.clientWidth,
                }; }),
            }));
            measurements.push({ enlarged, spacing, destination, ...bounds });
            expect.soft(bounds.rootWidth, 'document fits').toBeLessThanOrEqual(bounds.viewport);
            expect(bounds.elements.length).toBeGreaterThan(3);
            for (const element of bounds.elements) {
              expect.soft(element.left, `${element.text}: left`).toBeGreaterThanOrEqual(0);
              expect.soft(element.right, `${element.text}: right`).toBeLessThanOrEqual(bounds.viewport);
              expect.soft(element.width).toBeGreaterThan(0);
              expect.soft(element.scrollWidth, `${element.text}: full text`).toBeLessThanOrEqual(element.clientWidth + 1);
            }
            if (enlarged && spacing) await testInfo.attach(`publishing-${destination}`, {
              body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
            });
          }
        }
      }
      expect(errors).toEqual([]);
    } finally {
      await testInfo.attach('publishing-bounds', { body: JSON.stringify(measurements, null, 2), contentType: 'application/json' });
      await page.close();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
