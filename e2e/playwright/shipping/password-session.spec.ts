import { expect, test, type Page, type BrowserContext, type TestInfo } from '@playwright/test';
import { shippingPassword, signIn } from './sign-in';

test('shipping Console signs in, reads live data and revokes its password session', async ({ page, context }, info) => {
  const password = shippingPassword();

  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await signIn(page, password);
  await checkSession(page, context, password);
  await checkLivePages(page, info);
  await signOut(page);
  expect(errors).toEqual([]);
});

async function checkSession(page: Page, context: BrowserContext, password: string) {
  // Run requests in the page so the actual browser cookie and origin rules apply.
  const session = await page.evaluate(async () => {
    const response = await fetch('/api/v1/admin/auth/session');
    return { status: response.status, isAuthenticated: (await response.json()).isAuthenticated };
  });
  expect(session).toEqual({ status: 200, isAuthenticated: true });
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
    return { denied: denied.status, stillSignedIn: (await stillSignedIn.json()).isAuthenticated,
      accepted: accepted.status, after: (await after.json()).isAuthenticated, adminAfter: adminAfter.status };
  });
  expect(logout).toEqual({ denied: 400, stillSignedIn: true, accepted: 200, after: false, adminAfter: 401 });
}
