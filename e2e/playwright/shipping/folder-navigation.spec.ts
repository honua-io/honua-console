import { expect, test, type Page, type TestInfo } from '@playwright/test';
import { shippingPassword, signIn } from './sign-in';

const folderPath = '/console/operate/folders/shipping-folder';
const servicePath = '/console/operate/services/folder-navigation-check/settings';

test('shipping Folder → Service → Layer navigation survives reload', async ({ page }, info) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await signIn(page, shippingPassword());
  await checkMembership(page);
  await page.goto(folderPath);
  await expect(page.getByRole('heading', { name: 'Shipping Folder', exact: true, level: 1 })).toBeVisible();
  await expect(page.locator('[data-folder-count]')).toHaveText('1 Service · 2 Layers');
  await screenshot(page, info, 'folder');
  const service = page.getByRole('region', { name: 'Folder Services', exact: true })
    .getByRole('link', { name: 'folder-navigation-check', exact: true });
  await expect(service).toHaveAttribute('href', servicePath);
  await service.click();
  await expect(page).toHaveURL(new RegExp(`${servicePath}$`));
  await expect(page.locator('[data-service-loaded="true"] tr[data-layer-id="946801"]'))
    .toContainText('Folder navigation layer');
  await screenshot(page, info, 'service');
  await openLayer(page);
  const layerUrl = page.url();
  await checkLayerContext(page);
  await screenshot(page, info, 'layer');
  await page.reload();
  await expect(page).toHaveURL(layerUrl);
  await checkLayerContext(page);
  await screenshot(page, info, 'layer-reloaded');
  expect(errors).toEqual([]);
});

async function checkMembership(page: Page) {
  const reads = await page.evaluate(async () => {
    const connections = await fetch('/api/v1/admin/connections');
    const folders = await fetch('/api/v1/admin/folders');
    const services = await fetch('/api/v1/admin/services?folderId=shipping-folder');
    return { connections: { status: connections.status, body: await connections.json() }, folders: { status: folders.status, body: await folders.json() },
      services: { status: services.status, body: await services.json() } };
  });
  expect(reads.connections.status).toBe(200);
  expect(reads.connections.body.data).toEqual(expect.arrayContaining([
    expect.objectContaining({ connectionId: '94680100-0000-4000-8000-000000000001', name: 'shipping-folder-source' }),
  ]));
  expect(reads.folders.status).toBe(200);
  expect(reads.folders.body.data).toEqual(expect.arrayContaining([
    expect.objectContaining({ metadata: expect.objectContaining({ id: 'shipping-folder', name: 'ShippingFolder' }) }),
  ]));
  expect(reads.services.status).toBe(200);
  expect(reads.services.body.data.total).toBe(1);
  expect(reads.services.body.data.items).toEqual([
    expect.objectContaining({ serviceName: 'folder-navigation-check', folderId: 'shipping-folder', layerCount: 2 }),
  ]);
}

async function openLayer(page: Page) {
  const browse = page.getByRole('button', { name: 'Browse services', exact: true });
  if (await browse.isVisible()) await browse.click();
  const tree = page.getByRole('tree', { name: 'Services and layers', exact: true });
  const folder = tree.locator('[data-publication-key="folder:shipping-folder"]');
  await expect(folder).toBeVisible();
  if (await folder.getAttribute('aria-expanded') !== 'true') await folder.locator('[data-tree-expand]').click();
  const service = tree.locator('[data-publication-key="service:folder-navigation-check"]');
  await expect(service).toHaveAttribute('data-tree-parent', 'folder:shipping-folder');
  if (await service.getAttribute('aria-expanded') !== 'true') await service.locator('[data-tree-expand]').click();
  const layer = tree.locator('[data-tree-parent="service:folder-navigation-check"][data-tree-label="Folder navigation layer"]');
  const link = layer.locator('[data-tree-link]');
  await expect(link).toHaveAttribute('href', /^\/console\/operate\/layers\/[^/?#]+$/);
  await link.click();
}

async function checkLayerContext(page: Page) {
  await expect(page.locator('[data-layer-loaded="true"]')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Folder navigation layer', exact: true, level: 1 })).toBeVisible();
  const breadcrumb = page.getByRole('navigation', { name: 'Breadcrumb', exact: true });
  await expect(breadcrumb.getByRole('link', { name: 'Home', exact: true })).toHaveAttribute('href', '/console/');
  await expect(breadcrumb.getByRole('link', { name: 'Shipping Folder (ShippingFolder)', exact: true })).toHaveAttribute('href', folderPath);
  await expect(breadcrumb.getByRole('link', { name: 'folder-navigation-check', exact: true })).toHaveAttribute('href', servicePath);
  await expect(breadcrumb.locator('li[aria-current="page"] summary')).toHaveText('Folder navigation layer');
}

async function screenshot(page: Page, info: TestInfo, name: string) {
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await info.attach(name, { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
}

test('shipping folder catalog rejects an independent anonymous request', async ({ request }) => {
  for (const path of ['/api/v1/admin/folders', '/api/v1/admin/services?folderId=shipping-folder']) {
    const response = await request.get(path);
    expect(response.status()).toBe(401);
  }
});
