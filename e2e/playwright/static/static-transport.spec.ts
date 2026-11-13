import { expect, test } from '@playwright/test';
import { recordCspViolations } from './csp-violations';

for (const crossOrigin of [false, true]) {
  // The cross-origin fixture serves its API over plain-HTTP loopback, which the production CSP's
  // connect-src (https:) deliberately refuses; this test qualifies transport, not the policy.
  test.describe(crossOrigin ? 'cross-origin fixture' : 'same-origin fixture', () => {
    test.use({ bypassCSP: crossOrigin });
    test(`published WASM uses ${crossOrigin ? 'cross-origin bearer' : 'same-origin cookie'} transport`, async ({ page, context }, testInfo) => {
      testInfo.annotations.push({ type: 'contract-fixture', description: 'Password sign-in against the K1 contract fixture; real server authentication is qualified separately.' });
      const browserOrigin = `http://127.0.0.1:${crossOrigin ? 5186 : 5185}`;
      const apiOrigin = crossOrigin ? 'http://127.0.0.1:5187' : browserOrigin;
      const errors: string[] = [];
      const failedApi: string[] = [];
      const cspViolations = await recordCspViolations(page);
      page.on('pageerror', error => errors.push(error.message));
      page.on('response', response => {
        const path = new URL(response.url()).pathname;
        if (path.startsWith('/api/') && response.status() >= 400
          && !(path.endsWith('/auth/session') && response.status() === 401)) {
          failedApi.push(`${response.status()} ${response.url()}`);
        }
      });
      const connections = page.waitForResponse(response =>
        response.url().startsWith(`${apiOrigin}/api/v1/admin/connections`) && response.request().method() === 'GET');
      await page.goto(`${browserOrigin}/console/auth/signin?returnTo=%2Foperate%2Fconnections`);
      await page.getByLabel('Password', { exact: true }).fill('fixture-password');
      await testInfo.attach('sign-in-form', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
      await page.getByRole('button', { name: 'Continue', exact: true }).click();
      const response = await connections;
      expect(response.status()).toBe(200);
      await expect(page.getByRole('heading', { name: /^Data connections$/i })).toBeVisible();
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
        expect((await context.cookies(apiOrigin)).filter(cookie => cookie.name === 'console-fixture-session')).toEqual([]);
        expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toContain('console-fixture-bearer');
        expect(response.headers()['access-control-allow-origin']).toBe(browserOrigin);
      } else {
        expect(headers.cookie).toContain('console-fixture-session=issued');
        expect(headers.authorization).toBeUndefined();
        expect(await page.evaluate(() => document.cookie)).not.toContain('console-fixture-session');
      }
      const sharedRequest = page.waitForResponse(result =>
        result.url().startsWith(`${apiOrigin}/api/v1/admin/connections`) && result.request().method() === 'GET');
      const sharedResult = await page.evaluate(async () => {
        const transport = await import(new URL('js/console-browser-transport.mjs', document.baseURI).href);
        const session = await transport.captureConsoleSession();
        const result = await session.fetch('api/v1/admin/connections', {
          headers: { Authorization: 'Bearer untrusted', 'X-API-Key': 'untrusted' },
        });
        const body = await result.json();
        await session.assertCurrent();
        return { principalId: session.principalId, revision: session.revision,
          keys: Object.keys(session), status: result.status, connectionId: body.data[0].connectionId };
      });
      expect(sharedResult.principalId).toBe('fixture-operator');
      expect(sharedResult.revision).toBeGreaterThan(0);
      expect(sharedResult.keys.sort()).toEqual(['assertCurrent', 'fetch', 'principalId', 'revision']);
      expect(sharedResult.status).toBe(200);
      expect(sharedResult.connectionId).toBe('11111111-1111-1111-1111-111111111111');
      const sharedHeaders = await (await sharedRequest).request().allHeaders();
      expect(sharedHeaders['x-api-key']).toBeUndefined();
      expect(sharedHeaders.authorization).toBe(crossOrigin ? 'Bearer console-fixture-bearer' : undefined);
      if (crossOrigin) expect(sharedHeaders.cookie).toBeUndefined();
      else expect(sharedHeaders.cookie).toContain('console-fixture-session=issued');
      expect(errors).toEqual([]);
      expect(failedApi).toEqual([]);
      expect(await cspViolations()).toEqual([]);
      await testInfo.attach('published-transport', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
    });
  });
}

test('same-origin OIDC broker redirects and returns beneath the Console mount', async ({ page, context }, testInfo) => {
  testInfo.annotations.push({ type: 'contract-fixture', description: 'Server-brokered OIDC contract fixture; real provider qualification is separate.' });
  const origin = 'http://127.0.0.1:5188';
  const errors: string[] = [];
  const exchanges: string[] = [];
  const cspViolations = await recordCspViolations(page);
  const callback = page.waitForResponse(response => new URL(response.url()).pathname === "/admin/auth/callback");
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    if (request.url().endsWith('/providers/entra/token')) exchanges.push(request.postData() ?? '');
  });
  await page.goto(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fconnections`);
  await expect(page.getByLabel('Password', { exact: true })).toHaveCount(0);
  await testInfo.attach('oidc-sign-in', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  await page.getByRole('button', { name: 'Continue with Organization', exact: true }).click();
  await expect(page).toHaveURL(`${origin}/console/operate/connections`);
  await expect(page.getByRole('link', { name: 'Browser transport fixture', exact: true })).toBeVisible();
  const callbackResponse = await callback;
  expect(callbackResponse.status()).toBe(302);
  expect(callbackResponse.headers().location).toBe('/console/operate/connections');
  expect(exchanges).toEqual([]);
  expect(await page.evaluate(() => JSON.stringify({ ...localStorage, ...sessionStorage }))).not.toMatch(/fixture-code|fixture-pending|accessToken/);
  expect((await context.cookies(origin)).find(cookie => cookie.name === 'console-fixture-session')?.httpOnly).toBe(true);
  await testInfo.attach('oidc-return', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
  const replay = await context.request.get(callbackResponse.url(), { maxRedirects: 0 });
  expect(replay.status()).toBe(403);
  expect(exchanges).toEqual([]);
  expect(errors).toEqual([]);
  expect(await cspViolations()).toEqual([]);
});
