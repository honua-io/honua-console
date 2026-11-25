import { expect, test } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

test('anonymous /environments/new avoids redirect loops and /console/catalog preserves pathbase', async ({ page }) => {
  await page.goto(`${origin}/console/environments/new`);
  await expect(page).toHaveURL(`${origin}/console/environments/new`);
  await expect(page.getByRole('heading', { name: 'Add environment', exact: true })).toBeVisible();

  await page.route(`${origin}/api/v1/capabilities/manifest`, route => route.fulfill({
    json: {
      schemaVersion: 'honua.capability_manifest.v1',
      capabilities: [{ id: 'console.content.persistent', lifecycle: 'Implemented', optInRequired: false, available: true, supported: true }],
    },
  }));
  await signInWithFixturePassword(page, origin, '/catalog');
  await page.waitForURL(url => url.pathname === '/console/catalog');
  await expect(page.locator('form.console-filter-bar')).toHaveAttribute('action', '/console/catalog');
});

test('high-stakes anonymous write requires confirmation and API key secret offers one-click copy', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name === 'mobile', 'Mutation confirmation flow runs once in desktop project');
  let accessPutCount = 0;

  await page.route(`${origin}/api/v1/admin/services**`, async route => {
    const url = new URL(route.request().url());
    if (url.pathname.endsWith('/access-policy') && route.request().method() === 'PUT') {
      accessPutCount++;
      await route.fulfill({
        json: {
          success: true,
          data: {
            serviceName: 'roads',
            enabledProtocols: ['FeatureServer'],
            availableProtocols: ['FeatureServer', 'OgcFeatures'],
            accessPolicy: { allowAnonymous: false, allowAnonymousWrite: true, allowedRoles: [], allowedWriteRoles: [] },
          },
        },
      });
      return;
    }
    if (url.pathname.endsWith('/settings')) {
      await route.fulfill({
        json: {
          success: true,
          data: {
            serviceName: 'roads',
            enabledProtocols: ['FeatureServer'],
            availableProtocols: ['FeatureServer', 'OgcFeatures'],
            accessPolicy: { allowAnonymous: false, allowAnonymousWrite: false, allowedRoles: [], allowedWriteRoles: [] },
          },
        },
      });
      return;
    }
    await route.fulfill({
      json: {
        success: true,
        data: {
          items: [{
            name: 'roads',
            folderId: null,
            enabledProtocols: ['FeatureServer'],
            layerCount: 1,
            allowAnonymous: false,
            layers: [{ layerId: 0, name: 'Roads', geometry: 'LineString', canonicalResourceId: 'res-1', canonicalResourceName: 'Roads' }],
          }],
          page: 1,
          pageSize: 100,
          total: 1,
          layerTotal: 1,
        },
      },
    });
  });

  await page.route(`${origin}/api/v1/admin/api-keys**`, async route => {
    if (route.request().method() === 'POST') {
      await route.fulfill({
        status: 201,
        json: {
          success: true,
          data: {
            apiKey: {
              id: 'key-new',
              name: 'CI Deploy',
              keyPrefix: 'hn_live_new',
              permissions: ['admin:read'],
              status: 'active',
            },
            key: 'hn_live_secret_value_12345',
          },
        },
      });
      return;
    }
    await route.fulfill({ json: { success: true, data: [] } });
  });

  await signInWithFixturePassword(page, origin, '/operate/services/roads/settings?tab=access');
  await page.waitForURL(url => url.pathname === '/console/operate/services/roads/settings');

  await page.locator('#service-access-tab').click();
  const writeCheckbox = page.locator('[data-allow-anonymous-write]');
  await expect(writeCheckbox).toBeVisible();
  await writeCheckbox.check();
  await page.locator('[data-save-access]').click();

  const dialog = page.locator('[data-console-confirm]');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('Allow anonymous write access?');
  await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  expect(accessPutCount).toBe(0);

  await page.locator('[data-save-access]').click();
  await page.locator('[data-console-confirm-accept]').click();
  await expect.poll(() => accessPutCount).toBe(1);

  await page.goto(`${origin}/console/operate/settings/api-keys`);
  await page.getByLabel('Name', { exact: true }).fill('CI Deploy');
  await page.getByLabel('Permissions', { exact: true }).fill('admin:read');
  await page.getByRole('button', { name: 'Create key', exact: true }).click();
  await expect(page.locator('#api-key-secret')).toHaveValue('hn_live_secret_value_12345');
  await expect(page.locator('[data-console-copy]')).toBeVisible();
});
