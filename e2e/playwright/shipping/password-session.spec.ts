import { expect, test, type Page, type BrowserContext, type TestInfo } from '@playwright/test';
import { readFileSync } from 'node:fs';

test('shipping Console signs in, reads live data and revokes its password session', async ({ page, context }, info) => {
  const passwordFile = process.env.HONUA_SHIPPING_PASSWORD_FILE;
  if (!passwordFile) throw new Error('HONUA_SHIPPING_PASSWORD_FILE is required');
  const password = readFileSync(passwordFile, 'utf8').trim();
  if (!password) throw new Error('The quickstart password file is empty');

  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await signIn(page, password);
  await checkSession(page, context, password);
  await checkLivePages(page, info);
  await signOut(page);
  expect(errors).toEqual([]);
});

async function signIn(page: Page, password: string) {
  await page.goto('/console/auth/signin');
  const passwordInput = page.locator('input[type="password"]');
  await expect(passwordInput).toBeVisible();
  const signInResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === '/api/v1/admin/auth/session'
    && response.request().method() === 'POST');
  await passwordInput.fill(password);
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  expect((await signInResponse).status()).toBe(204);
  await expect(page).not.toHaveURL(/\/auth\/signin/);
  await expect(passwordInput).toHaveCount(0);
}

async function checkSession(page: Page, context: BrowserContext, password: string) {
  // Run requests in the page so the actual browser cookie and origin rules apply.
  const sessionStatus = await page.evaluate(async () => (await fetch('/api/v1/admin/auth/session')).status);
  expect(sessionStatus).toBe(200);
  const cookies = await context.cookies();
  const sessionCookie = cookies.find(cookie => cookie.name === 'honua_admin_session');
  expect(Boolean(sessionCookie?.httpOnly && sessionCookie.secure && sessionCookie.sameSite === 'Strict')).toBe(true);
  const storage = await page.evaluate(() => JSON.stringify({ local: { ...localStorage }, session: { ...sessionStorage } }));
  // Compare a boolean so a failure cannot print the password or storage contents.
  expect(storage.includes(password), 'password must stay out of browser storage').toBe(false);
}

async function checkLivePages(page: Page, info: TestInfo) {
  for (const [route, endpoint, heading] of [
    ['/console/', '/api/v1/admin/services', 'Welcome'],
    ['/console/operate/connections', '/api/v1/admin/connections', 'Data connections'],
  ]) {
    const read = page.waitForResponse(response =>
      new URL(response.url()).pathname.replace(/\/$/, '') === endpoint && response.request().method() === 'GET');
    await page.goto(route);
    expect((await read).status()).toBe(200);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Console could not load' })).toHaveCount(0);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    await expect(page.getByText('Loading Honua Console…', { exact: true })).toHaveCount(0);
    await expect(page).not.toHaveURL(/\/auth\/signin/);
    await info.attach(route === '/console/' ? 'home' : 'connections', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  }
}

async function signOut(page: Page) {
  const logout = await page.evaluate(async () => {
    const denied = await fetch('/api/v1/admin/auth/logout', { method: 'POST' });
    const stillSignedIn = await fetch('/api/v1/admin/auth/session');
    const csrf = await (await fetch('/api/v1/admin/auth/csrf')).json();
    const accepted = await fetch('/api/v1/admin/auth/logout', {
      method: 'POST', headers: { 'X-Honua-CSRF': csrf.requestToken },
    });
    const after = await fetch('/api/v1/admin/auth/session');
    const adminAfter = await fetch('/api/v1/admin/connections');
    return { denied: denied.status, stillSignedIn: stillSignedIn.status,
      accepted: accepted.status, after: after.status, adminAfter: adminAfter.status };
  });
  expect(logout).toEqual({ denied: 400, stillSignedIn: 200, accepted: 200, after: 401, adminAfter: 401 });
}
