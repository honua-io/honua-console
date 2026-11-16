import { expect, test, type Page } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const spec = JSON.stringify({
  $schema: 'https://vega.github.io/schema/vega-lite/v6.json',
  width: 240, height: 140, mark: 'bar',
  data: { values: [{ category: 'east', amount: 12 }, { category: 'west', amount: 30 }] },
  encoding: {
    x: { field: 'category', type: 'nominal' },
    y: { field: 'amount', type: 'quantitative' },
  },
});

async function openChartPanel(page: Page, v4Enabled: boolean) {
  await page.route('**/console/config.json', async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), v4Enabled } });
  });
  await signInWithFixturePassword(page, 'http://127.0.0.1:5185', '/studio/report');
  await page.getByRole('button', { name: 'New report', exact: true }).click();
  await page.getByRole('button', { name: '+ Panel', exact: true }).click();
  return page.getByLabel('Spec (JSON)', { exact: true });
}

for (const v4Enabled of [false, true]) {
  test(`report chart clears and restores its spec with v4=${v4Enabled}`, async ({ page }, testInfo) => {
    const input = await openChartPanel(page, v4Enabled);
    const chart = page.locator('.chart-preview');
    await input.fill(spec);
    await input.blur();
    await expect(chart).toHaveAttribute('data-chart-bound', 'true');
    const bars = chart.locator('.mark-rect.role-mark path');
    await expect(bars).toHaveCount(2);
    await expect(bars.first()).toHaveAttribute('aria-label', 'category: east; amount: 12');

    await input.fill('');
    await input.blur();
    await expect(chart).toHaveAttribute('data-chart-bound', 'false');
    await expect(chart.locator('.chart-preview-canvas svg')).toHaveCount(0);
    await expect(chart.locator('.chart-preview-schematic')).toBeVisible();
    await testInfo.attach('chart-cleared', {
      body: await chart.screenshot(), contentType: 'image/png',
    });

    await input.fill(spec);
    await input.blur();
    await expect(chart).toHaveAttribute('data-chart-bound', 'true');
    await expect(bars).toHaveCount(2);
    await expect(bars.last()).toHaveAttribute('aria-label', 'category: west; amount: 30');
    await testInfo.attach('chart-restored', {
      body: await chart.screenshot(), contentType: 'image/png',
    });
  });

  test(`removed report panel ignores a delayed module with v4=${v4Enabled}`, async ({ page }) => {
    const input = await openChartPanel(page, v4Enabled);
    let release!: () => void;
    let requested!: () => void;
    const held = new Promise<void>(resolve => { release = resolve; });
    const importing = new Promise<void>(resolve => { requested = resolve; });
    await page.route('**/_content/Honua.Console.Shell/chart-preview.js', async route => {
      const response = await route.fetch();
      requested();
      await held;
      await route.fulfill({ response });
    });
    await input.fill(spec);
    await input.blur();
    await importing;
    await page.getByRole('button', { name: 'Remove panel', exact: true }).click();
    release();
    await page.waitForResponse(response => response.url().endsWith('/chart-preview.js'));
    await expect(page.locator('.chart-preview')).toHaveCount(0);
    // Adding another chart uses the resolved module and still binds normally.
    await page.getByRole('button', { name: '+ Panel', exact: true }).click();
    const replacement = page.getByLabel('Spec (JSON)', { exact: true });
    await replacement.fill(spec);
    await replacement.blur();
    await expect(page.locator('.chart-preview')).toHaveAttribute('data-chart-bound', 'true');
    await expect(page.locator('.chart-preview .mark-rect.role-mark path')).toHaveCount(2);
  });
}
