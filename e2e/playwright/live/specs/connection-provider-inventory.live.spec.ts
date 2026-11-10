import { test, expect } from '../admin-api';

// Run against the qualified default image, never a mocked capability response.
for (const width of [1280, 390]) {
  test(`default image exposes only installed connection providers at ${width}px`, async ({ page, admin }) => {
    await page.setViewportSize({ width, height: 900 });
    const manifest = await admin.getJson('/api/v1/capabilities/manifest');
    expect(manifest.schemaVersion).toBe('honua.capability_manifest.v1');
    expect(manifest.dataStores.installedProviders).toEqual(['postgis']);
    expect(manifest.dataStores.fullImageTagSuffix).toBe('-full');
    await page.goto('/operate/connections/new');
    const provider = page.getByRole('combobox', { name: 'Database type', exact: true });
    await expect(provider).toBeEnabled();
    await expect(provider.locator('option')).toHaveText(['PostgreSQL / PostGIS']);
    await expect(provider).toHaveValue('postgis');
    const upgrade = page.locator('[data-provider-upgrade]');
    await expect(upgrade).toContainText('not installed on this server');
    await expect(upgrade).toContainText('-full');
    const documentation = upgrade.getByRole('link', { name: 'How to switch server images' });
    await expect(documentation).toHaveAttribute('href', manifest.dataStores.documentationHref);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: test.info().outputPath(`connection-providers-${width}.png`), fullPage: true });
  });
}
