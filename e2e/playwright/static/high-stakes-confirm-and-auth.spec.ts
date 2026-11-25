import { expect, test, type Page, type Route } from '@playwright/test';
import { signInWithFixturePassword } from './fixture-sign-in';

const origin = 'http://127.0.0.1:5185';

function serviceSettingsPayload(allowAnonymousWrite: boolean) {
  return {
    success: true,
    data: {
      serviceName: 'roads',
      enabledProtocols: ['FeatureServer'],
      availableProtocols: ['FeatureServer', 'OgcFeatures'],
      accessPolicy: { allowAnonymous: false, allowAnonymousWrite, allowedRoles: [], allowedWriteRoles: [] },
    },
  };
}

function serviceListPayload() {
  return {
    success: true,
    data: {
      items: [{
        serviceName: 'roads',
        folderId: null,
        enabledProtocols: ['FeatureServer'],
        layerCount: 1,
        allowAnonymous: false,
      }],
      page: 1,
      pageSize: 100,
      total: 1,
      layerTotal: 1,
    },
  };
}

function createdApiKeyPayload() {
  return {
    success: true,
    data: {
      apiKey: {
        id: '11111111-1111-4111-8111-111111111111',
        name: 'CI Deploy',
        keyPrefix: 'hn_live_new',
        permissions: ['admin:read'],
        status: 'active',
        createdAt: '2026-11-24T01:00:00Z',
        updatedAt: '2026-11-24T01:00:00Z',
      },
      key: 'hn_live_secret_value_12345',
    },
  };
}

async function handleServiceRoute(route: Route, state: { accessPutCount: number }) {
  const { pathname } = new URL(route.request().url());
  if (pathname.endsWith('/access-policy') && route.request().method() === 'PUT') {
    state.accessPutCount++;
    await route.fulfill({ json: serviceSettingsPayload(true) });
    return;
  }
  if (pathname.endsWith('/settings')) {
    await route.fulfill({ json: serviceSettingsPayload(false) });
    return;
  }
  await route.fulfill({ json: serviceListPayload() });
}

async function registerHighStakesRoutes(page: Page, state: { accessPutCount: number }) {
  await page.route(`${origin}/api/v1/admin/services**`, route => handleServiceRoute(route, state));
  await page.route(`${origin}/api/v1/admin/api-keys**`, route => route.fulfill(
    route.request().method() === 'POST'
      ? { status: 201, json: createdApiKeyPayload() }
      : { json: { success: true, data: [] } },
  ));
}

async function verifyAnonymousWriteConfirmation(page: Page, state: { accessPutCount: number }) {
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
  expect(state.accessPutCount).toBe(0);

  await page.locator('[data-save-access]').click();
  await page.locator('[data-console-confirm-accept]').click();
  await expect.poll(() => state.accessPutCount).toBe(1);
}

async function verifyApiKeyCopyReveal(page: Page) {
  await page.goto(`${origin}/console/operate/settings/api-keys`);
  await page.getByLabel('Name', { exact: true }).fill('CI Deploy');
  await page.getByLabel('Permissions', { exact: true }).fill('admin:read');
  await page.getByRole('button', { name: 'Create key', exact: true }).click();
  await expect(page.locator('#api-key-secret')).toHaveValue('hn_live_secret_value_12345');
  await expect(page.locator('[data-console-copy]')).toBeVisible();
}

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
  const state = { accessPutCount: 0 };
  await registerHighStakesRoutes(page, state);
  await verifyAnonymousWriteConfirmation(page, state);
  await verifyApiKeyCopyReveal(page);
});
