import { test, expect } from '../admin-api';
import { seedImportedFolder } from '../imported-folder';

test('browse an imported folder and its layers with an isolated anonymous control', async ({ page, admin, browser }) => {
  test.setTimeout(480_000);
  const folder = await seedImportedFolder(admin);
  const listing = await admin.getJson(`/api/v1/admin/services?folderId=${encodeURIComponent(folder.id)}`);
  const services = listing.data.items;
  expect(listing.data.total).toBe(2);
  expect(services.every((service: any) => service.folderId === folder.id)).toBe(true);
  expect(services.map((service: any) => service.serviceName).sort()).toEqual(folder.services.map(service => service.name).sort());

  await page.goto('/operate/publishing');
  const tree = page.getByRole('tree', { name: 'Services and layers' });
  await expect(tree).toBeVisible();
  const folderRow = tree.locator(`[data-publication-key="folder:${encodeURIComponent(folder.id)}"]`);
  await expect(folderRow).toHaveAccessibleName(`${folder.name} 2 services`);
  for (const service of folder.services) {
    if (await folderRow.getAttribute('aria-expanded') !== 'true') {
      await folderRow.getByRole('button', { name: `Expand ${folder.name}`, exact: true }).click();
    }
    await expect(folderRow).toHaveAttribute('aria-expanded', 'true');
    const row = tree.locator(`[data-publication-key="service:${encodeURIComponent(service.name)}"]`);
    await expect(row).toHaveAttribute('data-tree-parent', `folder:${encodeURIComponent(folder.id)}`);
    await expect(row).toContainText(service.name);
    await row.locator('[data-tree-expand]').click();
    const layerKey = `service:${encodeURIComponent(service.name)}:layer:${folder.connectionId.toLowerCase()}-layer-${service.layerId}`;
    const layerRow = tree.locator(`[data-publication-key="${layerKey}"]`);
    await expect(layerRow).toHaveCount(1);
    const layerLabel = await layerRow.getAttribute('data-tree-label');
    expect(layerLabel).toBeTruthy();
    await layerRow.locator('[data-tree-link]').click();
    await expect(page.locator(`[data-layer-resource-id="${folder.connectionId.toLowerCase()}-layer-${service.layerId}"][data-layer-loaded]`)).toBeVisible();
    await expect(page.getByRole('heading', { name: layerLabel!, exact: true, level: 1 })).toBeVisible();
    const breadcrumb = page.getByRole('navigation', { name: 'Breadcrumb' });
    await expect(breadcrumb.locator('ol > li:first-child > a')).toHaveAttribute('href', '/');
    await expect(breadcrumb.locator('ol > li:nth-last-child(3)')).toHaveText(folder.name);
    await expect(breadcrumb.locator('ol > li:nth-last-child(2) > a')).toHaveAttribute(
      'href', `/operate/services/${encodeURIComponent(service.name)}/settings`);
    await expect(breadcrumb.locator('ol > li:last-child summary')).toHaveText(layerLabel!);
    await expect(tree.locator('[aria-selected="true"]')).toHaveAttribute('data-tree-parent', `service:${encodeURIComponent(service.name)}`);
    await page.reload();
    await expect(page.locator(`[data-layer-resource-id="${folder.connectionId.toLowerCase()}-layer-${service.layerId}"][data-layer-loaded]`)).toBeVisible();
    await expect(page.getByRole('heading', { name: layerLabel!, exact: true, level: 1 })).toBeVisible();
    await expect(breadcrumb.locator('ol > li:nth-last-child(3)')).toHaveText(folder.name);
    await expect(breadcrumb.locator('ol > li:nth-last-child(2) > a')).toHaveAttribute(
      'href', `/operate/services/${encodeURIComponent(service.name)}/settings`);
    await expect(breadcrumb.locator('ol > li:last-child summary')).toHaveText(layerLabel!);
  }

  const anonymous = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  try {
    const blocked = await anonymous.request.get(page.url(), { maxRedirects: 0 });
    expect(blocked.status()).toBe(302);
    const redirect = new URL(blocked.headers().location, page.url());
    expect(redirect.pathname).toBe('/auth/login');
    expect(redirect.searchParams.get('returnTo')).toBe(new URL(page.url()).pathname);
    const body = await blocked.text();
    expect(body).not.toContain('data-publication-tree');
    expect(body).not.toContain(folder.name);
    for (const service of folder.services) expect(body).not.toContain(service.name);
    const denied = await anonymous.request.get(`${admin.serverUrl}/api/v1/admin/folders`);
    expect([401, 403]).toContain(denied.status());
  } finally {
    await anonymous.close();
  }
});
