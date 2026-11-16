import { expect, test } from '@playwright/test';

const routes = [
  '/studio/automations',
  '/studio/automations/new',
  '/studio/automations/missing',
  '/studio/automations/missing/versions',
  '/operate/geoprocessing',
  '/operate/geoprocessing/missing',
];

for (const route of routes) {
  test(`anonymous workflow route offers sign-in first: ${route}`, async ({ page }, testInfo) => {
    const origin = 'http://127.0.0.1:5185';
    const errors: string[] = [];
    const resourceRequests: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => {
      const path = new URL(request.url()).pathname;
      if (path.startsWith('/api/') && !path.startsWith('/api/v1/admin/auth/')
        && path !== '/api/v1/capabilities/manifest') {
        resourceRequests.push(path);
      }
    });
    // Match the live signed-out session response without requiring an installed server.
    await page.route('**/api/v1/admin/auth/session', intercepted => intercepted.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ isAuthenticated: false, claims: [] }),
    }));

    await page.goto(`${origin}/console${route}`);
    await expect(page).toHaveURL(`${origin}/console/auth/signin?returnTo=${encodeURIComponent(route)}`);
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in', exact: true })).toBeVisible();
    await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cancel', exact: true })).toHaveAttribute('href', `/console${route}`);
    await expect(page.getByText(/Temporarily unavailable|not bound|cannot reach the Honua API/i)).toHaveCount(0);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    expect(resourceRequests).toEqual([]);
    expect(errors).toEqual([]);
    await testInfo.attach('anonymous-workflow-sign-in', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  });
}
