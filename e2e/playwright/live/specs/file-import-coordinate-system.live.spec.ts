import { randomUUID } from 'node:crypto';
import { test, expect } from '../admin-api';

for (const target of [null, 4326]) {
  test(`projected upload ${target === null ? 'keeps source' : 'explicitly reprojects'}`, async ({ page }) => {
    await page.goto('/operate/data/new?source=file');
    const entry = page.getByRole('link', { name: 'Choose dataset file' });
    await expect(entry).toHaveAttribute('href', '/operate/resources/import');
    await entry.click();
    await expect(page.getByText(/Supported:/)).toBeVisible();
    await page.locator('input[type=file]').setInputFiles({ name: 'projected.csv', mimeType: 'text/csv',
      buffer: Buffer.from('id,name,wkt\n1,projected,"SRID=2193;POINT(1740000 5420000)"\n') });
    await expect(page.locator('[data-import-detected-system]')).toContainText('EPSG:2193');
    await expect(page.locator('[data-import-detected-system]')).toContainText(/NZGD2000|New Zealand/);
    await expect(page.getByRole('radio', { name: 'Keep source (recommended)' })).toBeChecked();
    await expect(page.getByLabel('Find source coordinate system', { exact: true })).toHaveCount(0);
    if (target !== null) {
      await page.getByRole('radio', { name: 'Reproject to…' }).check();
      await expect(page.getByRole('button', { name: 'Import file', exact: true })).toBeDisabled();
      await page.getByLabel('Find target coordinate system', { exact: true }).fill(String(target));
      await page.getByRole('button', { name: 'Search coordinate systems' }).click();
      await page.getByRole('combobox', { name: 'Target coordinate system', exact: true }).selectOption(String(target));
    }
    const table = `e2e_crs_${randomUUID().replaceAll('-', '')}`;
    await page.getByPlaceholder('parcels').fill(table);
    await page.getByRole('button', { name: 'Import file', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'File imported', exact: true })).toBeVisible({ timeout: 60_000 });
    await expect(page.locator('[data-imported-table]')).toHaveText(table);
    await expect(page.locator('[data-import-stored-system]')).toHaveText(`EPSG:${target ?? 2193}`);
  });
}

test('undeclared source needs an explicit system before import', async ({ page }) => {
  await page.goto('/operate/resources/import');
  await expect(page.getByText(/Supported:/)).toBeVisible();
  await page.locator('input[type=file]').setInputFiles({ name: 'unknown.csv', mimeType: 'text/csv',
    buffer: Buffer.from('id,name,wkt\n1,projected,"POINT(1740000 5420000)"\n') });
  await expect(page.locator('[data-import-detected-system]')).toContainText('could not be identified');
  await expect(page.getByRole('button', { name: 'Import file', exact: true })).toBeDisabled();
  await page.getByLabel('Find source coordinate system', { exact: true }).fill('2193');
  await page.getByRole('button', { name: 'Search coordinate systems' }).click();
  await page.getByRole('combobox', { name: 'Source coordinate system', exact: true }).selectOption('2193');
  await expect(page.getByRole('radio', { name: 'Keep source (recommended)' })).toBeChecked();
  const table = `e2e_crs_${randomUUID().replaceAll('-', '')}`;
  await page.getByPlaceholder('parcels').fill(table);
  await page.getByRole('button', { name: 'Import file', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'File imported', exact: true })).toBeVisible({ timeout: 60_000 });
  await expect(page.locator('[data-imported-table]')).toHaveText(table);
  await expect(page.locator('[data-import-stored-system]')).toHaveText('EPSG:2193');
});
