import { expect, test } from '@playwright/test';

for (const locale of ['en-us@posix', 'fr-FR']) {
  test(`static bundle starts with browser locale ${locale}`, async ({ browser }, testInfo) => {
    const context = await browser.newContext({
      baseURL: testInfo.project.use.baseURL,
      viewport: testInfo.project.use.viewport,
      locale: 'fr-FR',
    });
    try {
      await context.addInitScript(language => {
        Object.defineProperty(navigator, 'language', { get: () => language });
        Object.defineProperty(navigator, 'languages', { get: () => [language] });
      }, locale);
      const page = await context.newPage();
      const errors: string[] = [];
      const warnings: string[] = [];
      page.on('pageerror', error => errors.push(error.message));
      page.on('console', message => {
        if (message.type() === 'warning') warnings.push(message.text());
      });
      await page.goto('/console/');
      expect(await page.evaluate(() => navigator.languages[0])).toBe(locale);
      await expect(page.getByRole('heading', { name: 'Console could not load' })).toBeVisible();
      await expect(page.getByText('Loading Honua Console…', { exact: true })).toHaveCount(0);
      await expect(page.locator('#blazor-error-ui')).toBeHidden();
      expect(errors).toEqual([]);
      expect(warnings.filter(message => message.includes('Unsupported browser locale'))).toEqual(
        locale === 'en-us@posix' ? ['Unsupported browser locale; starting Honua Console with en-US.'] : []);
      await testInfo.attach('startup-locale', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
    } finally {
      await context.close();
    }
  });
}
