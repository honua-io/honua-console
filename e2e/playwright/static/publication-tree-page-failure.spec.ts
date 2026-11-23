import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';
import { startReflowFixture } from './reflow-fixture';

const total = 250;

function servicePage(page: number, pageSize: number) {
  const start = (page - 1) * pageSize;
  const items = Array.from({ length: Math.max(0, Math.min(pageSize, total - start)) }, (_, offset) => ({
    serviceName: `service-${String(start + offset).padStart(3, '0')}`, layerCount: 2,
  }));
  return { success: true, data: { items, page, pageSize, total, layerTotal: total * 2 } };
}

test('v4 tree keeps loaded services when a later service page fails, and retries the rest', async ({ page }, testInfo) => {
  const fixture = await startReflowFixture(page, { v4Enabled: true });
  let failSecondPage = true;
  await page.route('**/api/v1/admin/services?*', async route => {
    const query = new URL(route.request().url()).searchParams;
    const pageNumber = Number(query.get('page') ?? '1');
    if (pageNumber === 2 && failSecondPage) {
      await route.fulfill({ status: 503, json: { success: false, message: 'Service page unavailable.' } });
      return;
    }
    await route.fulfill({ json: servicePage(pageNumber, Number(query.get('pageSize') ?? '100')) });
  });
  try {
    await page.setViewportSize({ width: 1280, height: 900 });
    await signInWithFixturePassword(page, fixture.origin, '/operate/services');
    const tree = page.locator('[data-publication-tree]');
    const retry = page.locator('[data-tree-page-retry]');
    await expect(retry).toBeVisible();
    await expect(tree).toBeVisible();
    await expect(tree.locator("[data-publication-key='service:service-099']")).toBeVisible();
    await expect(page.locator('.console-tree-live')).toHaveText('Incomplete');
    await expect(page.locator('.console-tree-foot')).toContainText('100 of 250 services');
    await testInfo.attach('tree-page-failure-1280', { body: await page.screenshot(), contentType: 'image/png' });

    failSecondPage = false;
    await retry.click();
    await expect(page.locator('.console-tree-live')).toHaveText('Live');
    await expect(page.locator('.console-tree-foot')).toContainText('250 services');
    await expect(page.locator('.console-tree-foot')).not.toContainText('100 of');
    await expect(retry).toHaveCount(0);
    expect(fixture.errors).toEqual([]);
  } finally {
    await page.close();
    await fixture.close();
  }
});
