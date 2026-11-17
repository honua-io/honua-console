import { expect, test } from '@playwright/test';

for (const returnTo of ['/console/', '/console/operate/connections']) {
  test(`keyboard reload recovers ${returnTo} when the API returns`, async ({ page }, testInfo) => {
    let sessionRequests = 0;
    let apiHealthy = false;
    await page.route('**/api/v1/admin/auth/session', async route => {
      sessionRequests++;
      await route.fulfill({
        status: apiHealthy ? 200 : 503,
        json: apiHealthy ? { isAuthenticated: false, claims: [] } : {},
      });
    });
    await page.goto(`/console/auth/signin?returnTo=${encodeURIComponent(returnTo)}`);
    await expect(page.getByRole('heading', { name: 'Console could not load' })).toBeVisible();
    expect(sessionRequests).toBe(1);
    const originalUrl = page.url();
    apiHealthy = true;
    const recoveredSession = page.waitForResponse(response =>
      new URL(response.url()).pathname === '/api/v1/admin/auth/session' && response.status() === 200);
    await page.getByRole('link', { name: 'Reload Console' }).focus();
    await page.keyboard.press('Enter');
    await recoveredSession;
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Console could not load' })).toHaveCount(0);
    expect(sessionRequests).toBe(2);
    expect(page.url()).toBe(originalUrl);
    await testInfo.attach('recovered-console', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  });
}
