import { expect, test } from '@playwright/test';

const origin = 'http://127.0.0.1:5185';
const item = {
  itemId: 'reflow-item', title: 'CoastalFloodResource'.repeat(12), description: 'Description'.repeat(30),
  publisherName: 'Owner'.repeat(25), tags: ['longtag'.repeat(30)], distributions: [],
};

for (const width of [320, 390, 1280]) {
  for (const fontSize of [16, 32]) {
    test(`Public Share metadata wraps at ${width}px with ${fontSize}px text`, async ({ page, context }, testInfo) => {
      test.skip(testInfo.project.name !== (width === 1280 ? 'desktop' : 'mobile'));
      const mutations: string[] = [];
      await context.addCookies([{ name: 'console-fixture-session', value: 'issued', url: origin }]);
      await page.route(`${origin}/api/**`, async route => {
        const request = route.request();
        if (request.method() !== 'GET') {
          mutations.push(`${request.method()} ${new URL(request.url()).pathname}`);
          await route.abort();
        } else if (request.url().endsWith('/api/v1/capabilities/manifest')) {
          await route.fulfill({ json: { schemaVersion: 'honua.capability_manifest.v1', capabilities: [{
            id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false,
            available: true, supported: true,
          }] } });
        } else if (request.url().endsWith('/api/v1/open-data/datasets/reflow-item')) {
          await route.fulfill({ json: { success: true, data: item } });
        } else {
          await route.continue();
        }
      });
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`${origin}/console/share/public/items/reflow-item`);
      await expect(page.locator('[data-share-opendata-landing]')).toBeVisible();
      await page.addStyleTag({ content: `html { font-size: ${fontSize}px !important; } .share-opendata-intro { font-size: ${14 * fontSize / 16}px !important; }` });
      await expect(page.getByRole('heading', { name: item.title, exact: true })).toBeVisible();
      await expect(page.locator('.share-opendata-intro')).toHaveText(item.description);
      await expect(page.locator('.share-opendata-intro')).toHaveCSS('font-size', `${14 * fontSize / 16}px`);
      await expect(page.locator('.share-opendata-v4 .public-item-tags li')).toHaveText(`#${item.tags[0]}`);
      await expect(page.locator('.public-item-meta')).toContainText(item.publisherName);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      const back = page.getByRole('link', { name: 'Open-data catalog', exact: true });
      await back.focus();
      await expect(back).toBeFocused();
      await expect(back).toHaveAttribute('href', '/console/share/public');
      expect(mutations).toEqual([]);
      await testInfo.attach(`public-share-${width}-${fontSize}`, {
        body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
      });
    });
  }
}
