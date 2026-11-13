import { expect, test } from '@playwright/test';

for (const crossOrigin of [false, true]) {
  test.describe(crossOrigin ? 'Studio bearer transport' : 'Studio cookie transport', () => {
    test.use({ bypassCSP: crossOrigin });
    test('installed Studio bundle uses the authenticated native session', async ({ page }, testInfo) => {
      testInfo.annotations.push({ type: 'contract-fixture', description:
        'Released Studio mounted through the published Console transport; live server qualification is separate.' });
      const origin = `http://127.0.0.1:${crossOrigin ? 5186 : 5185}`;
      const apiOrigin = crossOrigin ? 'http://127.0.0.1:5187' : origin;
      await page.goto(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fconnections`);
      await page.getByLabel('Password', { exact: true }).fill('fixture-password');
      await page.getByRole('button', { name: 'Continue', exact: true }).click();
      await expect(page.getByRole('heading', { name: /^Data connections$/i })).toBeVisible();
      const bootstrap = page.waitForResponse(`${apiOrigin}/api/v1/studio/session`);
      await page.evaluate(async () => {
        const host = document.createElement('section');
        host.dataset.studioEmbed = '';
        host.innerHTML = '<p data-studio-status role="status"></p><div data-studio-mount></div>';
        document.body.replaceChildren(host);
        await import(new URL('js/studio-embed.mjs', document.baseURI).href);
      });
      await expect(page.locator('[data-studio-status]')).toHaveText('Connected to fixture-environment');
      await expect(page.locator('honua-studio-app')).toBeVisible();
      const packages = page.waitForResponse(response =>
        response.url().startsWith(`${apiOrigin}/api/v1/studio/content-items?`) && response.request().method() === 'GET');
      expect(await page.evaluate(async () => {
        const app = document.querySelector('honua-studio-app') as HTMLElement & {
          studioClient: { listPackages(): Promise<unknown[]> };
        };
        return app.studioClient.listPackages();
      })).toEqual([]);
      for (const response of [await bootstrap, await packages]) {
        expect(response.status()).toBe(200);
        const headers = await response.request().allHeaders();
        expect(headers['x-api-key']).toBeUndefined();
        expect(headers.authorization).toBe(crossOrigin ? 'Bearer console-fixture-bearer' : undefined);
        if (crossOrigin) expect(headers.cookie).toBeUndefined();
        else expect(headers.cookie).toContain('console-fixture-session=issued');
      }
      await testInfo.attach('installed-studio-native-session', {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
    });
  });
}
