import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

async function openNavRoute(page: Page, groupLabel: string, linkLabel: string) {
  const navToggle = page.getByRole('button', { name: 'Toggle navigation', exact: true });
  if (await navToggle.isVisible()) {
    await navToggle.click();
  }
  const menu = page.locator('nav[aria-label="Primary"] summary', { hasText: groupLabel });
  await menu.click();
  await page.locator('nav[aria-label="Primary"]').getByRole('link', { name: linkLabel, exact: true }).click();
}

function fulfillProblem(status: number, title: string, headers?: Record<string, string>) {
  return {
    status,
    headers,
    contentType: 'application/problem+json',
    body: JSON.stringify({ title, status }),
  };
}

test('401 on a same-origin manager-bound route clears the session and redirects to sign-in', async ({ page }) => {
  let opsHealthRequested = false;
  await page.route(`${origin}/api/v1/admin/observability/ops-health`, route => {
    opsHealthRequested = true;
    return route.fulfill(fulfillProblem(401, 'Unauthorized'));
  });

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  await openNavRoute(page, 'Operate', 'Health');

  await expect.poll(() => opsHealthRequested).toBe(true);
  await expect(page).toHaveURL(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fhealth`);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByText('Permission required')).toHaveCount(0);
});

test('mid-session 401 on a Blazor serving route clears the session and prompts sign-in', async ({ page }) => {
  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');
  await expect(page.getByText('Browser transport fixture')).toBeVisible();

  let expiredServicesRequested = false;
  await page.route(`${origin}/api/v1/admin/services**`, route => {
    expiredServicesRequested = true;
    return route.fulfill(fulfillProblem(401, 'Unauthorized'));
  });

  await openNavRoute(page, 'Data', 'Services');

  await expect.poll(() => expiredServicesRequested).toBe(true);
  await expect(page).toHaveURL(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fservices`);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
});

test('mid-session 401 on a JS-hosted Studio transport clears the browser session and prompts sign-in', async ({ page }) => {
  let studioSessionExpired = false;
  await page.route(`${origin}/api/v1/studio/session`, route => {
    studioSessionExpired = true;
    return route.fulfill(fulfillProblem(401, 'Unauthorized'));
  });

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');
  await expect(page.getByText('Browser transport fixture')).toBeVisible();

  const statusText = await page.evaluate(async () => {
    const mod = await import(new URL('js/studio-session.mjs', document.baseURI).href);
    try {
      await mod.readStudioSession();
      return 'unexpected-success';
    } catch (error) {
      return error instanceof Error ? error.message : String(error);
    }
  });

  expect(studioSessionExpired).toBe(true);
  expect(statusText).toBe('Sign in to Honua to open Studio.');
  await expect(page).toHaveURL(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fconnections`);
  await expect(page.getByRole('heading', { level: 1, name: 'Sign in', exact: true })).toBeVisible();
});

test('403 on RBAC pages renders Permission required instead of API not bound', async ({ page }) => {
  await page.route(`${origin}/api/v1/console/access/**`, route => route.fulfill(fulfillProblem(403, 'Forbidden')));

  await signInWithFixturePassword(page, origin, '/operate/access');
  await page.waitForURL(url => url.pathname === '/console/operate/access');

  await expect(page.getByRole('heading', { level: 2, name: 'Permission required', exact: true })).toBeVisible();
  await expect(page.getByText('Access (RBAC) API is not bound')).toHaveCount(0);
  await expect(page.getByText(/\(contract:/i)).toHaveCount(0);

  await page.goto(`${origin}/console/operate/access/members`);
  await expect(page.getByRole('heading', { level: 2, name: 'Permission required', exact: true })).toBeVisible();
  await expect(page.getByText('Access (RBAC) API is not bound')).toHaveCount(0);
  await expect(page.getByText(/\(contract:/i)).toHaveCount(0);
});

test('429 on password sign-in shows rate-limit notice with Retry-After hint', async ({ page }) => {
  await page.route(`${origin}/api/v1/admin/auth/session`, route => {
    if (route.request().method() === 'POST') {
      return route.fulfill(fulfillProblem(429, 'Too Many Requests', {
        'Retry-After': '45',
        'Honua-Retryable': 'true',
      }));
    }
    return route.continue();
  });

  await page.goto(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fconnections`);
  await page.getByLabel('Password', { exact: true }).fill('fixture-password');
  await page.getByRole('button', { name: 'Continue', exact: true }).click();

  const alert = page.locator('[role="alert"]');
  await expect(alert).toBeVisible();
  await expect(alert).toContainText('Too many sign-in attempts');
  await expect(alert).toContainText('Wait 45 seconds and try again.');
  await expect(alert).not.toContainText('Check the server connection');
});

test('429 with Retry-After on an authenticated route renders rate-limit notice and recovers on retry', async ({ page }) => {
  let throttled = true;
  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route => {
    if (throttled) {
      return route.fulfill(fulfillProblem(429, 'Too Many Requests', {
        'Retry-After': '30',
        'Honua-Retryable': 'true',
      }));
    }
    return route.continue();
  });

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');

  const notice = page.locator('[data-connection-notice]');
  await expect(notice).toBeVisible();
  await expect(notice.getByRole('heading', { level: 2, name: 'Too many requests', exact: true })).toBeVisible();
  await expect(notice).toContainText('Wait 30 seconds and try again.');

  throttled = false;
  await notice.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(notice).toHaveCount(0);
  await expect(page.getByText('Browser transport fixture')).toBeVisible();
});

test('503 with Honua-Retryable true renders temporary unavailable banner and recovers on retry', async ({ page }) => {
  let unavailable = true;
  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route => {
    if (unavailable) {
      return route.fulfill(fulfillProblem(503, 'Service Unavailable', {
        'Honua-Retryable': 'true',
        'Retry-After': '20',
      }));
    }
    return route.continue();
  });

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');

  const notice = page.locator('[data-connection-notice]');
  await expect(notice).toBeVisible();
  await expect(notice.getByRole('heading', { level: 2, name: 'Service temporarily unavailable', exact: true })).toBeVisible();
  await expect(notice).toContainText('Wait 20 seconds and try again.');

  unavailable = false;
  await notice.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(notice).toHaveCount(0);
  await expect(page.getByText('Browser transport fixture')).toBeVisible();
});

test('503 with Honua-Retryable false renders non-retryable unavailable notice without retry button', async ({ page }) => {
  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route =>
    route.fulfill(fulfillProblem(503, 'Service Unavailable', {
      'Honua-Retryable': 'false',
    })));

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');

  const notice = page.locator('[data-connection-notice]');
  await expect(notice).toBeVisible();
  await expect(notice.getByRole('heading', { level: 2, name: 'Service unavailable', exact: true })).toBeVisible();
  await expect(notice).toContainText('retrying will not resolve this request');
  await expect(notice.getByRole('button', { name: 'Try again', exact: true })).toHaveCount(0);
  await expect(notice.getByRole('link', { name: 'Review environment', exact: true })).toBeVisible();
});

test('offline network failure shows connection notice and recovers when connectivity returns', async ({ page }) => {
  let offline = true;
  await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route => {
    if (offline) {
      return route.abort('internetdisconnected');
    }
    return route.continue();
  });

  await signInWithFixturePassword(page, origin, '/operate/connections');
  await page.waitForURL(url => url.pathname === '/console/operate/connections');

  const notice = page.locator('[data-connection-notice]');
  await expect(notice).toBeVisible();
  await expect(notice.getByRole('heading', { level: 2, name: 'Could not read data connections', exact: true })).toBeVisible();
  await expect(notice).toContainText('Your data has not been confirmed empty.');

  offline = false;
  await notice.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(notice).toHaveCount(0);
  await expect(page.getByText('Browser transport fixture')).toBeVisible();
});
