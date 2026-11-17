import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

for (const v4Enabled of [false, true]) {
  test(`responsive navigation retains keyboard focus with v4=${v4Enabled}`, async ({ page }, testInfo) => {
    await page.route('**/console/config.json', async route => {
      const response = await route.fetch();
      await route.fulfill({ response, json: { ...await response.json(), v4Enabled } });
    });
    await page.setViewportSize({ width: 1600, height: 900 });
    await signInWithFixturePassword(page, 'http://127.0.0.1:5185', '/operate/connections');
    await page.waitForURL(url => url.pathname === '/console/operate/connections');
    const home = page.locator('#console-primary > a').first();
    const toggle = page.locator('[data-navigation-toggle]');
    await expect(home).toBeVisible();
    await home.focus();
    await expect(home).toBeFocused();
    await page.setViewportSize({ width: 390, height: 900 });
    await expect(toggle).toBeFocused();
    await expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await page.keyboard.press('Enter');
    await expect(home).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(toggle).toBeFocused();
    await testInfo.attach('navigation-resize-390', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await page.setViewportSize({ width: 1600, height: 900 });
    await expect(home).toBeFocused();
    // Enlarging text collapses the container query without changing the viewport.
    await page.evaluate(() => document.documentElement.style.fontSize = '32px');
    await expect(toggle).toBeFocused();
    await page.evaluate(() => document.documentElement.style.fontSize = '16px');
    await expect(home).toBeFocused();
    // Responsive chrome must leave focus in page content alone.
    const heading = page.getByRole('heading', { level: 1 });
    await heading.focus();
    await page.setViewportSize({ width: 390, height: 900 });
    await expect(heading).toBeFocused();
    await page.setViewportSize({ width: 1280, height: 900 });
    await expect(heading).toBeFocused();
    await testInfo.attach('navigation-resize-1280', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
  });
}
