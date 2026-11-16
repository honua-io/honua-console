import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';

test('filled shared inputs remain readable in both themes', async ({ page }, testInfo) => {
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
      handleApi: async (request, response) => {
        response.setHeader('Content-Type', 'application/json');
        const path = new URL(request.url!, 'http://localhost').pathname;
        if (path.endsWith('/auth/session')) {
          response.end(JSON.stringify({ isAuthenticated: false, claims: [] }));
        } else {
          response.writeHead(404).end('{}');
        }
      },
    });
  try {
    const address = server.address();
    if (!address || typeof address === 'string') throw new Error('Console fixture did not bind a TCP port');
    await page.goto(`http://127.0.0.1:${address.port}/console/auth/signin`);
    await expect(page.locator('html')).toHaveAttribute('data-console-theme-enabled', 'true');
    const password = page.locator('input[type="password"]');
    await password.fill('synthetic-readability-check');
    const search = page.locator('#console-tree-filter');
    if (await search.isVisible()) await search.fill('synthetic-service-search');
    const toggle = page.locator('[data-console-theme-toggle]');
    for (const theme of ['light', 'dark', 'light']) {
      if (await page.locator('html').getAttribute('data-theme') !== theme) await toggle.click();
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
      const audit = await new AxeBuilder({ page }).include('.console-input')
        .withRules(['color-contrast']).analyze();
      expect(audit.incomplete, `${theme} input contrast must be decidable`).toEqual([]);
      expect(audit.violations, `${theme} filled input contrast`).toEqual([]);
      await expect(password).toHaveValue('synthetic-readability-check');
      await testInfo.attach(`shared-inputs-${theme}`, {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
    }
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});
