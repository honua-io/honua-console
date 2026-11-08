import { test, expect } from '../admin-api';
import { seedImportedFolder } from '../imported-folder';

test('browse an imported folder and its layers with an isolated anonymous control', async ({ page, admin, browser }) => {
  test.setTimeout(480_000);
  const folder = await seedImportedFolder(admin);
  const listing = await admin.getJson('/api/v1/admin/services');
  const services = listing.data.filter((service: any) => service.folderId === folder.id);
  expect(services.map((service: any) => service.serviceName).sort()).toEqual(folder.services.map(service => service.name).sort());

  await page.goto('/operate/publishing');
  const tree = page.getByRole('tree', { name: 'Services and layers' });
  await expect(tree).toBeVisible();
  const folderRow = tree.locator(`[data-publication-key="folder:${encodeURIComponent(folder.id)}"]`);
  await expect(folderRow).toHaveAccessibleName(`${folder.name} 2 services`);
  await folderRow.getByRole('button', { name: `Expand ${folder.name}`, exact: true }).click();

  for (const service of folder.services) {
    const row = tree.locator(`[data-publication-key="service:${encodeURIComponent(service.name)}"]`);
    await expect(row).toHaveAttribute('data-tree-parent', `folder:${encodeURIComponent(folder.id)}`);
    await expect(row).toContainText(service.name);
    await row.locator('[data-tree-expand]').click();
    const layerRow = tree.locator(`[data-tree-parent="service:${encodeURIComponent(service.name)}"]`);
    await expect(layerRow).toHaveCount(1);
    await layerRow.locator('[data-tree-link]').click();
    await expect(page.locator('[data-layer-loaded="true" i]')).toBeVisible();
    await expect(page.getByRole('navigation', { name: 'Breadcrumb' })).toContainText(folder.name);
    await expect(tree.locator('[aria-selected="true"]')).toHaveAttribute('data-tree-parent', `service:${encodeURIComponent(service.name)}`);
  }

  const anonymous = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  try {
    const anonymousPage = await anonymous.newPage();
    await anonymousPage.goto(page.url());
    await expect(anonymousPage.locator('[data-publication-navigation]')).toContainText(/sign in|forbidden|missing|not configured/i);
    await expect(anonymousPage.locator(`[data-tree-label="${folder.name}"]`)).toHaveCount(0);
    for (const service of folder.services) {
      await expect(anonymousPage.locator(`[data-tree-label="${service.name}"]`)).toHaveCount(0);
    }
    const denied = await anonymous.request.get(`${admin.serverUrl}/api/v1/admin/folders`);
    expect([401, 403]).toContain(denied.status());
  } finally {
    await anonymous.close();
  }
});
