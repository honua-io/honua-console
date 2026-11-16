import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

test('unsupported conversation stays explicit and never starts a legacy workspace', async ({ page }, testInfo) => {
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
  const toggle = page.locator('[data-conversation-toggle]');
  await expect(toggle).toBeVisible();
  await toggle.focus();
  await page.keyboard.press('Enter');
  const drawer = page.locator('#console-conversation-drawer');
  await expect(drawer).toHaveAttribute('open', '');
  await expect(drawer).toHaveAttribute('data-workspace-availability', 'unavailable');
  await expect(drawer).not.toHaveAttribute('data-mounted', 'true');
  await expect(drawer.getByRole('status')).toContainText('Conversation is unavailable in this Console deployment.');
  await expect(drawer.locator('textarea, [data-workspace-start], [data-workspace-connect], [data-workspace-send]')).toHaveCount(0);
  const close = drawer.getByRole('button', { name: 'Close conversation', exact: true });
  await expect(close).toBeFocused();
  await expect(drawer).toHaveCSS('position', 'fixed');
  const bounds = await drawer.boundingBox();
  const viewport = page.viewportSize()!;
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(viewport.width);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(viewport.height);
  if (testInfo.project.name === 'mobile') {
    await expect(drawer).toHaveAttribute('aria-modal', 'true');
    await page.keyboard.press('Tab');
    expect(await drawer.evaluate(element => element.contains(document.activeElement))).toBe(true);
  }
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await testInfo.attach('conversation-unavailable', {
    body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
  });
  await close.focus();
  await page.keyboard.press('Escape');
  await expect(drawer).not.toHaveAttribute('open', '');
  await expect(toggle).toBeFocused();
  await toggle.click();
  await expect(drawer).toHaveAttribute('open', '');
  await close.click();
  await expect(drawer).not.toHaveAttribute('open', '');
  await expect(toggle).toBeFocused();
  expect(workspaceRequests).toEqual([]);
  expect(errors).toEqual([]);
});
