import { expect, test } from '@playwright/test';

test('Console boots against AWS and renders the live resource/publication tree', async ({ page }, testInfo) => {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(`console: ${message.text()}`);
  });
  page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));

  const response = await page.goto('/operate/data', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Data & Layers', exact: true })).toBeVisible();
  await expect(page.locator('[data-operate-data]')).toBeVisible();

  const resources = page.locator('[data-resource-node]');
  await expect(resources.first()).toBeVisible();
  expect(await resources.count(), 'the AWS-bound Console should render live resources').toBeGreaterThan(0);
  await expect(page.locator('.console-state-error')).toHaveCount(0);

  // Draft-only resources legitimately have no publications. Exercise the
  // first server-confirmed running resource instead of relying on API order.
  const firstResource = page.locator('.resource-tree__node--running').first();
  await expect(firstResource).toBeVisible();
  await firstResource.getByRole('button', { name: /^Expand / }).click();
  const publications = firstResource.locator('[data-publication]');
  await expect(publications.first()).toBeVisible();
  await publications.first().locator('button').click();
  await expect(page.locator('[data-preview-route]')).toBeVisible();

  if (errors.length > 0) {
    testInfo.annotations.push({ type: 'console-errors', description: errors.join('\n') });
  }
  expect(errors, `console/page errors on the AWS-bound resource tree:\n${errors.join('\n')}`).toEqual([]);
});

test('Console catalog resolves against the same AWS environment without a read failure', async ({ page }) => {
  const response = await page.goto('/catalog', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Catalog', exact: true }).first()).toBeVisible();
  await expect(page.locator('.console-state-error')).toHaveCount(0);

  const tableRows = page.locator('.console-content-table tbody tr');
  const serverBridge = page.locator('[data-catalog-server-bridge]');
  await expect
    .poll(async () => (await tableRows.count()) + (await serverBridge.count()))
    .toBeGreaterThan(0);
});
