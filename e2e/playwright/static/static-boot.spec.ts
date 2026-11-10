import { expect, test } from '@playwright/test';

for (const route of ['/console/', '/console/operate/connections']) {
  test(`static bundle boots ${route} with an explicit unavailable API state`, async ({ page }, testInfo) => {
    const errors: string[] = [];
    const failedAssets: string[] = [];
    const runtime: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('response', response => {
      const path = new URL(response.url()).pathname;
      if (path.startsWith('/console/') && response.status() >= 400) failedAssets.push(path);
      if (path.endsWith('.wasm') && response.ok()) runtime.push(path);
    });
    await page.goto(route);
    await expect(page.getByRole('heading', { name: 'Console could not load' })).toBeVisible();
    await expect(page.getByText('Check the API connection and your sign-in session, then reload.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Reload Console' })).toBeVisible();
    await expect(page.getByText('Loading Honua Console…', { exact: true })).toHaveCount(0);
    expect(runtime.length).toBeGreaterThan(0);
    expect(failedAssets).toEqual([]);
    expect(errors).toEqual([]);
    await testInfo.attach('static-bundle', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  });
}
