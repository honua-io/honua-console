import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

test('unfinished catalog admin is hidden while discovery endpoints, API keys, and 1280px nav work cleanly', async ({ page }, testInfo) => {
  await page.route(`${origin}/api/v1/capabilities/manifest`, route => route.fulfill({
    json: {
      schemaVersion: 'honua.capability_manifest.v1',
      capabilities: [{ id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false, available: true, supported: true }],
    },
  }));
  await signInWithFixturePassword(page, origin, '/');
  await page.waitForURL(url => url.pathname === '/console/');

  // Zero catalog admin or metadata preview links appear in top navigation or Home.
  await expect(page.locator('a[href$="/catalogs"]:not([href*="/operate/catalogs"]), a[href*="/catalogs/"]:not([href*="/operate/catalogs"]), a[href*="/metadata/"]')).toHaveCount(0);
  await expect(page.locator('[data-navigation-menu="Catalogs"], [data-navigation-menu="Protocols"]')).toHaveCount(0);

  if (testInfo.project.name === 'desktop') {
    await page.setViewportSize({ width: 1280, height: 900 });
    await expect(page.locator('#console-primary')).toBeVisible();
    await expect(page.locator('[data-navigation-toggle]')).toBeHidden();
    await page.locator('[data-navigation-menu="Data"] > summary').click();
    await expect(page.locator('[data-navigation-menu="Data"] a[href="/console/operate/catalogs"]')).toBeVisible();
    await expect(page.locator('[data-navigation-menu="Data"] a[href="/console/share/manage"]')).toBeVisible();
    await page.locator('[data-navigation-menu="Settings"] > summary').click();
    await expect(page.locator('[data-navigation-menu="Settings"] a[href="/console/operate/settings/api-keys"]')).toBeVisible();
  }

  // Direct navigation to hidden preview routes returns Page not found with no leaked headings or links.
  for (const hiddenRoute of ['/console/catalogs', '/console/catalogs/open-data', '/console/metadata/fields']) {
    await page.goto(`${origin}${hiddenRoute}`);
    await expect(page.getByRole('heading', { name: 'Page not found', exact: true })).toBeVisible();
    await expect(page.locator('a[href$="/catalogs"]:not([href*="/operate/catalogs"]), a[href*="/catalogs/"]:not([href*="/operate/catalogs"])')).toHaveCount(0);
  }

  // Protocol discovery endpoints remain reachable with clear Discovery endpoints heading.
  await page.goto(`${origin}/console/operate/catalogs`);
  await expect(page.getByRole('heading', { name: 'Discovery endpoints', exact: true })).toBeVisible();
});
