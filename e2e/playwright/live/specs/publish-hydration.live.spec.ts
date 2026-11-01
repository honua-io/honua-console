import { expect, test } from '@playwright/test';

test('connection selection is unavailable until its interactive handler is ready', async ({ page }) => {
  await page.goto('/auth/login?returnTo=/version.json');
  await page.goto('/auth/server/login?profileId=local-dev&returnTo=%2Foperate%2Fconnections');
  await page.waitForURL(/:8443\//);
  await page.locator('#username').fill('customer');
  await page.locator('#password').fill('customer-live-proof-pw');
  await page.locator('#kc-login').click();
  await page.waitForURL('**/operate/connections');

  let release!: () => void;
  let blocked = false;
  const allowed = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/_framework/blazor.web*.js*', async route => {
    blocked = true;
    await allowed;
    await route.continue();
  });
  try {
    await page.goto('/operate/publishing/quick', { waitUntil: 'commit' });
    await expect.poll(() => blocked).toBe(true);
    const connection = page.getByLabel('Connection', { exact: true });
    await expect(connection).toBeVisible();
    await expect(connection).toBeDisabled();
    release();
    await expect(connection).toBeEnabled();
  } finally { release(); }
});
