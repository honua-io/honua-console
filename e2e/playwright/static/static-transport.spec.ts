import { expect, test } from '@playwright/test';

for (const crossOrigin of [false, true]) {
  test(`published WASM uses ${crossOrigin ? 'cross-origin bearer' : 'same-origin cookie'} transport`, async ({ page, context }, testInfo) => {
    testInfo.annotations.push({ type: 'contract-fixture', description: 'Issued-session transport gate; server sign-in is qualified separately.' });
    const browserOrigin = `http://127.0.0.1:${crossOrigin ? 5186 : 5185}`;
    const apiOrigin = crossOrigin ? 'http://127.0.0.1:5187' : browserOrigin;
    const errors: string[] = [];
    const failedApi: string[] = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('response', response => {
      if (new URL(response.url()).pathname.startsWith('/api/') && response.status() >= 400) {
        failedApi.push(`${response.status()} ${response.url()}`);
      }
    });
    await context.addCookies([{
      name: 'console-fixture-session', value: 'issued', url: browserOrigin,
      httpOnly: true, sameSite: 'Strict', secure: false,
    }]);
    const connections = page.waitForResponse(response =>
      response.url().startsWith(`${apiOrigin}/api/v1/admin/connections`) && response.request().method() === 'GET');
    await page.goto(`${browserOrigin}/console/operate/connections`);
    const response = await connections;
    expect(response.status()).toBe(200);
    await expect(page.getByRole('heading', { name: 'Data Connections', exact: true })).toBeVisible();
    const connectionLink = page.getByRole('link', { name: 'Browser transport fixture', exact: true });
    await expect(connectionLink).toBeVisible();
    await expect(connectionLink).toHaveAttribute('href', '/console/operate/connections/11111111-1111-1111-1111-111111111111');
    await expect(page.getByRole('link', { name: 'Add Connection', exact: true })).toHaveAttribute('href', '/console/operate/connections/new');
    await expect(page.getByText('Console could not load', { exact: true })).toHaveCount(0);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    const headers = await response.request().allHeaders();
    expect(headers['x-api-key']).toBeUndefined();
    if (crossOrigin) {
      expect(headers.authorization).toBe('Bearer console-fixture-bearer');
      expect(headers.cookie).toBeUndefined();
      expect(response.headers()['access-control-allow-origin']).toBe(browserOrigin);
    } else {
      expect(headers.cookie).toContain('console-fixture-session=issued');
      expect(headers.authorization).toBeUndefined();
      expect(await page.evaluate(() => document.cookie)).not.toContain('console-fixture-session');
    }
    expect(errors).toEqual([]);
    expect(failedApi).toEqual([]);
    await testInfo.attach('published-transport', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  });
}
