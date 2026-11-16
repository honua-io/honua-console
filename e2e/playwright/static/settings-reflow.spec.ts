import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { settingsFixture } from './settings-reflow-fixture';
import { applyTextMode, textModes } from './reflow-typography';

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

type SettingsContext = { port: number; v4Enabled: boolean; testInfo: TestInfo };

async function checkSettingsRoute(page: Page, route: string, context: SettingsContext) {
  await page.goto(`http://127.0.0.1:${context.port}/console/operate/${route}`);
  await expect(page.getByRole('heading', { name: route.endsWith('api-keys') ? 'API keys' : 'Settings', exact: true })).toBeVisible();
  if (route.endsWith('api-keys') && context.v4Enabled) await expect(page.locator('#api-key-name')).toBeVisible();
  if (route.endsWith('api-keys') && !context.v4Enabled) await expect(page.locator('.console-state')).toContainText('Unsupported');
  for (const mode of textModes) {
    const style = await applyTextMode(page, mode);
    await checkContainment(page);
    if (route.endsWith('api-keys') && context.v4Enabled) await checkTableKeyboardAccess(page);
    await context.testInfo.attach(`${route.replaceAll('/', '-')}-${mode}-v4-${context.v4Enabled}`, {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await style.evaluate(element => element.remove());
  }
}

async function checkSettingsRoutes(page: Page, context: SettingsContext) {
  for (const route of ['settings', 'settings/api-keys']) await checkSettingsRoute(page, route, context);
}

for (const v4Enabled of [false, true]) {
  test(`Settings and API keys reflow without hiding controls (v4=${v4Enabled})`, async ({ page }, testInfo) => {
    const { server, mutations } = await settingsFixture({ v4Enabled });
    try {
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
      await checkSettingsRoutes(page, { port: address.port, v4Enabled, testInfo });
      expect(mutations, 'read-only reflow checks do not create or revoke keys').toEqual([]);
    } finally {
      await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
    }
  });
}
