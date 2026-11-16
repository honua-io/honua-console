import { test, expect } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';

const authModes = ['anonymous', 'arcgis-token', 'token', 'basic', 'oauth'];
const textModes = [
  { name: 'baseline', enlarged: false, spacing: false },
  { name: 'spacing', enlarged: false, spacing: true },
  { name: 'enlarged', enlarged: true, spacing: false },
  { name: 'enlarged-spacing', enlarged: true, spacing: true },
];

for (const v4Enabled of [false, true]) {
  test(`service import fields reflow with enlarged text, v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const server = await startStaticBundleServer(
      fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0,
      { configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled },
        handleApi: (...args) => api(...args) },
    );
    const origin = `http://127.0.0.1:${(server.address() as { port: number }).port}`;
    const api = createContractApi({ allowedOrigin: origin });
    const pageErrors: string[] = [];
    page.on('pageerror', error => pageErrors.push(error.message));
    const measurements: unknown[] = [];
    try {
      await signInWithFixturePassword(page, origin, '/console/operate/import/service');
      const auth = page.getByRole('combobox', { name: 'Authentication mode' });
      await expect(auth).toBeVisible();
      await page.addStyleTag({ content: `html[data-test-spacing] * {
        line-height: 1.5 !important; letter-spacing: .12em !important; word-spacing: .16em !important;
      } html[data-test-spacing] p { margin-bottom: 2em !important; }` });
      for (const mode of textModes) {
        await page.evaluate(({ enlarged, spacing }) => {
          document.documentElement.style.fontSize = enlarged ? '200%' : '';
          document.documentElement.toggleAttribute('data-test-spacing', spacing);
        }, mode);
        for (const authMode of authModes) {
          await auth.selectOption(authMode);
          await expect(page.locator('[data-auth-fields]')).toHaveCount(authMode === 'anonymous' ? 0 : 1);
          const bounds = await page.locator('.operate-import-page').evaluate(section => ({
            viewport: innerWidth, rootWidth: document.documentElement.scrollWidth,
            fields: [...section.querySelectorAll('.studio-field-grid label, .console-input, .studio-field-grid small')]
              .map(element => { const rect = element.getBoundingClientRect();
                const panel = element.closest('.console-panel')!.getBoundingClientRect(); return {
                tag: element.tagName, left: rect.left, right: rect.right, width: rect.width,
                panelLeft: panel.left, panelRight: panel.right,
              }; }),
          }));
          measurements.push({ textMode: mode.name, authMode, ...bounds });
          expect.soft(bounds.rootWidth, `${mode.name}/${authMode}: document`).toBeLessThanOrEqual(bounds.viewport);
          for (const field of bounds.fields) {
            expect.soft(field.left, `${mode.name}/${authMode}: ${field.tag} left`).toBeGreaterThanOrEqual(0);
            expect.soft(field.right, `${mode.name}/${authMode}: ${field.tag} right`).toBeLessThanOrEqual(bounds.viewport);
            expect.soft(field.width, `${mode.name}/${authMode}: ${field.tag} width`).toBeGreaterThan(0);
            expect.soft(field.left, `${mode.name}/${authMode}: ${field.tag} panel left`).toBeGreaterThanOrEqual(field.panelLeft);
            expect.soft(field.right, `${mode.name}/${authMode}: ${field.tag} panel right`).toBeLessThanOrEqual(field.panelRight);
          }
        }
      }
      await testInfo.attach('import-service-reflow', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
      expect(pageErrors).toEqual([]);
    } finally {
      await testInfo.attach('field-bounds', { body: JSON.stringify(measurements, null, 2), contentType: 'application/json' });
      await page.close();
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
