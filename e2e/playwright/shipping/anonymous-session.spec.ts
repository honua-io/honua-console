import { expect, test } from '@playwright/test';

test('shipping Console keeps signed-out admin reads unauthorized', async ({ page, context }, info) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/console/auth/signin');
  await expect(page.locator('input[type="password"]')).toBeVisible();

  const result = await page.evaluate(async () => {
    const session = await fetch('/api/v1/admin/auth/session');
    const statuses: Record<string, number> = {};
    for (const endpoint of ['services', 'connections', 'api-keys']) {
      const response = await fetch(`/api/v1/admin/${endpoint}`, { redirect: 'manual' });
      statuses[endpoint] = response.status;
    }
    return { sessionStatus: session.status, authenticated: (await session.json()).isAuthenticated, statuses };
  });
  expect(result).toEqual({
    sessionStatus: 200, authenticated: false,
    statuses: { services: 401, connections: 401, 'api-keys': 401 },
  });
  expect((await context.cookies()).some(cookie => cookie.name === 'honua_admin_session')).toBe(false);
  await expect(page.getByRole('heading', { name: 'Console could not load' })).toHaveCount(0);
  expect(errors).toEqual([]);
  await info.attach('signed-out', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
});

test('shipping sign-in preserves local destinations and rejects external return paths', async ({ page }, info) => {
  const returnTo = '/operate/settings?tab=configuration#policy';
  await page.goto(`/console/auth/signin?returnTo=${encodeURIComponent(returnTo)}`);
  await expect(page.locator('input[type="password"]')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Cancel', exact: true })).toHaveAttribute('href', `/console${returnTo}`);
  await expect(page.locator('dd').last()).toHaveText(returnTo);
  await info.attach('sign-in-return-path', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });

  for (const destination of ['https://outside.invalid/path', '//outside.invalid/path', '/%2e%2e/outside']) {
    await page.goto(`/console/auth/signin?returnTo=${encodeURIComponent(destination)}`);
    await expect(page.locator('input[type="password"]')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cancel', exact: true })).toHaveAttribute('href', '/console/');
    await expect(page.locator('dd').last()).toHaveText('/');
    expect(new URL(page.url()).pathname).toBe('/console/auth/signin');
  }
});
