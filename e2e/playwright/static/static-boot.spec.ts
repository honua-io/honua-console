import { expect, test } from '@playwright/test';
import { recordCspViolations } from './csp-violations';

for (const route of ['/console/', '/console/operate/connections']) {
  test(`static bundle boots ${route} with an explicit unavailable API state`, async ({ page }, testInfo) => {
    const errors: string[] = [];
    const frameworkErrors: string[] = [];
    const failedAssets: string[] = [];
    const runtime: string[] = [];
    const cspViolations = await recordCspViolations(page);
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => {
      if (message.type() === 'error' && message.text().includes('fail:')) frameworkErrors.push(message.text());
    });
    page.on('response', response => {
      const path = new URL(response.url()).pathname;
      if (path.startsWith('/console/') && response.status() >= 400) failedAssets.push(path);
      if (path.endsWith('.wasm') && response.ok()) runtime.push(path);
    });
    const sessionResponse = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/v1/admin/auth/session');
    await page.goto(route);
    expect((await sessionResponse).status()).toBe(404);
    await expect(page.getByRole('heading', { name: 'Console could not load' })).toBeVisible();
    await expect(page.getByText('Check the API connection and your sign-in session, then reload.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Reload Console' })).toBeVisible();
    await expect(page.getByText('Loading Honua Console…', { exact: true })).toHaveCount(0);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    expect(frameworkErrors.length).toBeGreaterThan(0);
    for (const error of frameworkErrors) {
      expect(error).toContain('HttpRequestException');
      expect(error).toContain('404');
    }
    expect(runtime.length).toBeGreaterThan(0);
    expect(failedAssets).toEqual([]);
    expect(errors).toEqual([]);
    await expect(page.locator('meta[http-equiv="Content-Security-Policy"]')).toHaveCount(1);
    expect(await cspViolations()).toEqual([]);
    await testInfo.attach('static-bundle', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  });
}
