import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

test('unsupported conversation is hidden from shipping navigation and never starts a legacy workspace', async ({ page }, testInfo) => {
  const workspaceRequests: string[] = [];
  const errors: string[] = [];
  page.on('request', request => {
    if (new URL(request.url()).pathname.startsWith('/api/agent-workspace')) {
      workspaceRequests.push(`${request.method()} ${new URL(request.url()).pathname}`);
    }
  });
  page.on('pageerror', error => errors.push(error.message));
  await signInWithFixturePassword(page, 'http://127.0.0.1:5185', '/operate/connections');
  await expect(page).toHaveURL('http://127.0.0.1:5185/console/operate/connections');
  await expect(page.locator('[data-console-location]')).toHaveAttribute('data-console-location', '/console/operate/connections');
  await expect(page.locator('[data-conversation-toggle]')).toHaveCount(0);
  const drawer = page.locator('#console-conversation-drawer');
  await expect(drawer).not.toHaveAttribute('open', '');
  await expect(drawer).toHaveAttribute('data-workspace-availability', 'unavailable');
  await expect(drawer).not.toHaveAttribute('data-mounted', 'true');
  await expect(drawer.locator('textarea, [data-workspace-start], [data-workspace-connect], [data-workspace-send]')).toHaveCount(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await testInfo.attach('conversation-hidden', {
    body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
  });
  expect(workspaceRequests).toEqual([]);
  expect(errors).toEqual([]);
});
