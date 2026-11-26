import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

test.describe('Authenticated error states', () => {
  test('401 on a same-origin manager-bound route clears the session and redirects to sign-in', async ({ page }) => {
    let expireSession = false;
    await page.route(`${origin}/api/v1/admin/observability/ops-health`, route => route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ title: 'Unauthorized', status: 401 }),
    }));
    await page.route(`${origin}/api/v1/admin/auth/session`, route => {
      if (expireSession && route.request().method() === 'GET') {
        return route.fulfill({ status: 401 });
      }
      return route.continue();
    });

    await signInWithFixturePassword(page, origin, '/operate/connections');
    await page.waitForURL(url => url.pathname === '/console/operate/connections');

    expireSession = true;
    await page.goto(`${origin}/console/operate/health`);
    await expect(page).toHaveURL(`${origin}/console/auth/signin?returnTo=%2Foperate%2Fhealth`);
    await expect(page.getByRole('heading', { level: 1, name: 'Sign in', exact: true })).toBeVisible();
    await expect(page.getByText('Permission required')).toHaveCount(0);
  });

  test('403 on RBAC pages renders Permission required instead of API not bound', async ({ page }) => {
    await page.route(`${origin}/api/v1/console/access/**`, route => route.fulfill({
      status: 403,
      contentType: 'application/json',
      body: JSON.stringify({ title: 'Forbidden', status: 403 }),
    }));

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
        return route.fulfill({
          status: 429,
          headers: {
            'Retry-After': '45',
            'Honua-Retryable': 'true',
          },
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Too Many Requests', status: 429 }),
        });
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

  test('503 with Honua-Retryable renders temporary unavailable banner with retry button', async ({ page }) => {
    await page.route(url => url.origin === origin && /^\/api\/v1\/admin\/connections\/?$/.test(url.pathname), route => route.fulfill({
      status: 503,
      headers: {
        'Honua-Retryable': 'true',
        'Retry-After': '20',
      },
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Service Unavailable', status: 503 }),
    }));

    await signInWithFixturePassword(page, origin, '/operate/connections');
    await page.waitForURL(url => url.pathname === '/console/operate/connections');

    const notice = page.locator('[data-connection-notice]');
    await expect(notice).toBeVisible();
    await expect(notice.getByRole('heading', { level: 2, name: 'Service temporarily unavailable', exact: true })).toBeVisible();
    await expect(notice).toContainText('Wait 20 seconds and try again.');
    await expect(notice.getByRole('button', { name: 'Try again', exact: true })).toBeVisible();
  });
});
