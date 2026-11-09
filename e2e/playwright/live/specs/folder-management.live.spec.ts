import { test, expect, type AdminApi } from '../admin-api';
import type { Locator } from '@playwright/test';
import { SOURCE_DB, sourceConnectionBody } from '../source-db';

async function folders(admin: AdminApi): Promise<any[]> {
  return (await admin.getJson('/api/v1/admin/folders')).data;
}

async function mutate(management: Locator, selector: string, state = 'Saved'): Promise<void> {
  const previous = Number(await management.getAttribute('data-folder-completions'));
  await management.locator(selector).click();
  await expect(management).toHaveAttribute('data-folder-completions', String(previous + 1));
  await expect(management.locator('[data-folder-result]')).toHaveAttribute('data-state', state);
}

test('manage Folder membership through the human UI and preserve saved public paths', async ({ page, admin, browser }, testInfo) => {
  test.setTimeout(300_000);
  const suffix = Math.random().toString(36).slice(2, 10);
  const folderName = `folder-${suffix}`;
  const renamed = `renamed-${suffix}`;
  const serviceName = `identity-${suffix}`;
  const publicName = `public-${suffix}`;
  const connection = await admin.createConnection(sourceConnectionBody(`folders-${suffix}`));

  try {
  await page.goto('/operate/services');
  const management = page.locator('[data-folder-management]');
  await management.locator('[data-folder-name]').fill(folderName);
  await management.locator('[data-folder-title]').fill('Transport data');
  await mutate(management, '[data-folder-save]');
  const created = (await folders(admin)).find(folder => folder.metadata.name === folderName);
  expect(created).toBeTruthy();
  await expect(management.locator(`option[value="${created.metadata.id}"]`)).toHaveCount(1);
  await expect(page.locator(`[data-publication-navigation] [data-publication-key=\"folder:${created.metadata.id}\"]`)).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('empty-folder.png') });

  await page.goto('/operate/publishing');
  await page.getByRole('button', { name: 'Create new service', exact: true }).click();
  await page.locator('[data-new-service-name]').fill(serviceName);
  await page.locator('[data-service-public-name]').fill(publicName);
  await page.locator('[data-publish-folder]').selectOption(`folder:${created.metadata.id}`);
  await page.locator('.publish-wizard-next').click();
  await page.locator('[data-connection-picker]').selectOption(connection.connectionId);
  await expect(page.locator('[data-table-picker]')).toBeEnabled({ timeout: 60_000 });
  await page.locator('[data-table-picker]').selectOption(SOURCE_DB.folderTable);
  await page.locator('[data-layer-name]').fill('Folder parcels');
  await page.locator('.publish-wizard-next').click();
  await page.locator('.publish-wizard-finish').click();
  await expect(page.locator('[data-publish-state="published"]')).toBeVisible({ timeout: 60_000 });
  const originalPath = `/rest/services/${folderName}/${publicName}/FeatureServer`;
  const original = await admin.getJson(`${originalPath}?f=json`);
  expect(original.layers).toHaveLength(1);
  await expect(page.locator('[data-url-preview]')).toContainText(originalPath);

  await page.goto('/operate/services');
  await management.locator('[data-folder-select]').selectOption(created.metadata.id);
  await management.locator('[data-folder-name]').fill(renamed);
  await management.locator('[data-folder-title]').fill('Renamed transport');
  await mutate(management, '[data-folder-save]');
  const canonicalPath = `/rest/services/${renamed}/${publicName}/FeatureServer`;
  expect((await admin.getJson(`${originalPath}?f=json`)).layers).toEqual(original.layers);
  expect((await admin.getJson(`${canonicalPath}?f=json`)).layers).toEqual(original.layers);

  await page.goto(`/operate/services/${serviceName}/settings`);
  await expect(management.locator('[data-service-folder]')).toHaveValue(created.metadata.id);
  await management.locator('[data-service-folder]').selectOption('');
  await mutate(management, '[data-service-move]');
  const root = await admin.getJson(`/rest/services/${publicName}/FeatureServer?f=json`);
  expect(root.layers).toEqual(original.layers);
  const query = await admin.getJson(`${originalPath}/${original.layers[0].id}/query?where=1%3D1&outFields=*&f=json`);
  expect(query.features).toHaveLength(3);

  await page.goto('/operate/services');
  await management.locator('[data-folder-select]').selectOption(created.metadata.id);
  await management.locator('[data-folder-title]').fill('Retained after conflict');
  const second = await page.context().newPage();
  await second.goto('/operate/services');
  const other = second.locator('[data-folder-management]');
  await other.locator('[data-folder-select]').selectOption(created.metadata.id);
  await other.locator('[data-folder-title]').fill('Other human edit');
  await mutate(other, '[data-folder-save]');
  await second.close();
  await mutate(management, '[data-folder-save]', 'Snapshot changed');
  await expect(management.locator('[data-folder-title]')).toHaveValue('Retained after conflict');
  await expect(management.locator('[data-folder-save]')).toBeDisabled();
  expect((await folders(admin)).find(folder => folder.metadata.id === created.metadata.id).metadata.title).toBe('Other human edit');
  await management.locator('[data-folder-refresh]').click();
  await expect(management.locator('[data-folder-save]')).toBeEnabled();
  await mutate(management, '[data-folder-save]');

  await management.locator('[data-folder-select]').selectOption(created.metadata.id);
  await management.locator('[data-folder-delete-confirm]').check();
  await mutate(management, '[data-folder-delete]');
  await expect(management.locator(`option[value="${created.metadata.id}"]`)).toHaveCount(0);
  expect((await folders(admin)).some(folder => folder.metadata.id === created.metadata.id && !folder.deleted)).toBe(false);

  const anonymous = await browser.newContext();
  try {
    const denied = await anonymous.request.post(`${admin.serverUrl}/api/v1/admin/folders`, {
      headers: { 'If-Match': '"untrusted"' }, data: { name: `denied-${suffix}` },
    });
    expect([401, 403]).toContain(denied.status());
    expect((await folders(admin)).some(folder => folder.metadata.name === `denied-${suffix}`)).toBe(false);
  } finally {
    await anonymous.close();
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await management.scrollIntoViewIfNeeded();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath('folder-management-mobile.png') });
  } finally {
    await admin.deleteConnection(connection.connectionId);
    // The isolated testbed's volume teardown removes its published Services and Layers.
  }
});
