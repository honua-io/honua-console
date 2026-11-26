import { expect, test, type Page } from '@playwright/test';
import type { IncomingMessage, ServerResponse } from 'node:http';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { alertingCapability, createContractApi } from '../static-contract-server.mjs';
import { signInWithFixturePassword } from './fixture-sign-in';
import { mockPopulatedConsoleApis } from './v4-populated-fixture';
import { populatedMembersFixture } from './populated-members-fixture';

const staticOrigin = 'http://127.0.0.1:5185';
const bundleDirectory = fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url));

async function readJsonBody(request: IncomingMessage) {
  let raw = '';
  for await (const chunk of request) raw += chunk;
  return raw ? JSON.parse(raw) : {};
}

function writeJson(response: ServerResponse, body: unknown, status = 200) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body));
}

async function closeServer(server: { close(cb: (err?: Error) => void): void }) {
  await new Promise<void>((resolve, reject) => server.close(error => (error ? reject(error) : resolve())));
}

test('layer permanent filter saves, persists, and clears via confirmation dialog', async ({ page }) => {
  let savedFilter: { expression: string; language: string } | null = null;
  await mockPopulatedConsoleApis(page, staticOrigin);
  await page.route(`${staticOrigin}/api/v1/admin/metadata/layers/1/filter`, async route => {
    if (route.request().method() === 'PUT') {
      const payload = route.request().postDataJSON();
      savedFilter = payload.permanentFilter ?? null;
    }
    await route.fulfill({ json: { success: true, data: { layerId: 1, permanentFilter: savedFilter } } });
  });

  await signInWithFixturePassword(page, staticOrigin, '/operate/layers/1/filter');
  await expect(page.locator('[data-filter-none]')).toBeVisible();

  await page.locator('[data-filter-language]').selectOption('cql2-text');
  await page.locator('[data-filter-expression]').fill("status = 'active'");
  await page.locator('[data-filter-save]').click();

  await expect(page.locator('[data-filter-result]')).toContainText('Saved');
  await expect(page.locator('[data-filter-current]')).toContainText('cql2-text');
  expect(savedFilter).toEqual({ expression: "status = 'active'", language: 'cql2-text' });

  await page.reload();
  await expect(page.locator('[data-filter-expression]')).toHaveValue("status = 'active'");
  await expect(page.locator('[data-filter-language]')).toHaveValue('cql2-text');

  await page.locator('[data-filter-clear]').click();
  await page.locator('[data-console-confirm-accept]').click();
  await expect(page.locator('[data-filter-none]')).toBeVisible();
  expect(savedFilter).toBeNull();
});

test('layer field mask (hidden + alias) persists across save and page reload', async ({ page }) => {
  const fields = [
    { name: 'road_name', type: 'string', alias: 'Road Name', hidden: false, domain: null, defaultValue: null },
    { name: 'owner_ssn', type: 'string', alias: 'Owner SSN', hidden: false, domain: null, defaultValue: null },
  ];
  await mockPopulatedConsoleApis(page, staticOrigin);
  await page.route(`${staticOrigin}/api/v1/admin/metadata/layers/1/fields`, async route => {
    if (route.request().method() === 'PUT') {
      const update = route.request().postDataJSON()?.fields?.[0];
      const match = fields.find(field => field.name === update?.name);
      if (match) {
        match.alias = update.alias ?? null;
        match.hidden = Boolean(update.hidden);
      }
    }
    await route.fulfill({ json: { success: true, data: { layerId: 1, fields } } });
  });

  await signInWithFixturePassword(page, staticOrigin, '/operate/layers/1');
  const sensitiveRow = page.locator('[data-field-row][data-field-name="owner_ssn"]');
  await expect(sensitiveRow).toBeVisible();
  await sensitiveRow.locator('[data-field-alias]').fill('Masked SSN');
  await sensitiveRow.locator('[data-field-hidden]').check();
  await sensitiveRow.locator('[data-field-save]').click();

  await expect(page.locator('[data-field-result]')).toContainText("hidden on 'owner_ssn'");
  expect(fields[1]).toMatchObject({ name: 'owner_ssn', alias: 'Masked SSN', hidden: true });

  await page.reload();
  const reloadedRow = page.locator('[data-field-row][data-field-name="owner_ssn"]');
  await expect(reloadedRow.locator('[data-field-alias]')).toHaveValue('Masked SSN');
  await expect(reloadedRow.locator('[data-field-hidden]')).toBeChecked();
});

interface AlertRuleState {
  rule: Record<string, unknown> | null;
}

async function handleAlertRuleCollection(
  request: IncomingMessage,
  response: ServerResponse,
  state: AlertRuleState,
) {
  const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
  if (path === '/api/v1/admin/services') {
    writeJson(response, { success: true, data: { items: [{ serviceName: 'roads', enabledProtocols: ['OgcFeatures'], layerCount: 1 }], page: 1, pageSize: 100, total: 1, layerTotal: 1 } });
    return true;
  }
  if (path === '/api/v1/admin/alerts/zones') {
    writeJson(response, { success: true, data: [] });
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules' && request.method === 'GET') {
    writeJson(response, { success: true, data: state.rule ? [state.rule] : [] });
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules' && request.method === 'POST') {
    const body = await readJsonBody(request);
    state.rule = { ...body, ruleId: 101, isActive: false };
    writeJson(response, { success: true, data: state.rule }, 201);
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules/test' && request.method === 'POST') {
    writeJson(response, { success: true, data: { isValid: true, errors: [], warnings: [], deliveryChannels: [], evaluatedAt: '2026-11-24T01:00:00Z' } });
    return true;
  }
  return false;
}

async function handleAlertRuleDetail(
  request: IncomingMessage,
  response: ServerResponse,
  state: AlertRuleState,
) {
  const path = new URL(request.url!, 'http://localhost').pathname.replace(/\/$/, '');
  if (path === '/api/v1/admin/alerts/rules/101' && request.method === 'GET') {
    writeJson(response, { success: true, data: state.rule });
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules/101' && request.method === 'PUT') {
    state.rule = { ...state.rule, ...(await readJsonBody(request)), ruleId: 101 };
    writeJson(response, { success: true, data: state.rule });
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules/101/health') {
    writeJson(response, { success: true, data: { ruleId: 101, activeIncidentCount: 0, deliveryFailureCount: 0, deliveryChannels: [], recentTriggers: [] } });
    return true;
  }
  if (path === '/api/v1/admin/alerts/rules/101/enabled' && request.method === 'PUT') {
    state.rule = { ...state.rule, isActive: Boolean((await readJsonBody(request)).enabled) };
    writeJson(response, { success: true, data: state.rule });
    return true;
  }
  return false;
}

function createAlertRuleFixtureHandler(allowedOrigin: string) {
  const fallback = createContractApi({ allowedOrigin, capabilities: [alertingCapability] });
  const state: AlertRuleState = { rule: null };
  return async (request: IncomingMessage, response: ServerResponse) => {
    if (await handleAlertRuleCollection(request, response, state)) return;
    if (await handleAlertRuleDetail(request, response, state)) return;
    await fallback(request, response);
  };
}

test('alert rule create navigates to detail, validates test before enabling, and enables delivery', async ({ page }) => {
  let handler: (request: IncomingMessage, response: ServerResponse) => Promise<void> = async () => {};
  const server = await startStaticBundleServer(bundleDirectory, 0, {
    configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
    handleApi: (request, response) => handler(request, response),
  });
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
  const origin = `http://127.0.0.1:${address.port}`;
  handler = createAlertRuleFixtureHandler(origin);

  try {
    await signInWithFixturePassword(page, origin, '/operate/alerts/rules/new');
    await page.locator('[data-rule-service]').fill('roads');
    await page.locator('[data-rule-layer]').fill('1');
    await page.locator('[data-rule-name]').fill('High latency watch');
    await page.locator('[data-rule-subject]').fill('p95_ms');
    await page.locator('[data-rule-operator]').fill('gt');
    await page.locator('[data-rule-threshold]').fill('250');
    await page.locator('[data-rule-channels]').fill('webhook');
    await page.locator('[data-rule-create-submit]').click();

    await page.waitForURL(url => url.pathname === '/console/operate/alerts/rules/101');
    await expect(page.getByRole('heading', { level: 1, name: 'High latency watch', exact: true })).toBeVisible();
    await expect(page.locator('[data-rule-enable]')).toBeDisabled();

    await page.locator('[data-rule-test]').click();
    await expect(page.locator('[data-rule-test-result]')).toContainText('Draft validation passed');
    await expect(page.locator('[data-rule-enable]')).toBeEnabled();

    await page.locator('[data-rule-enable]').click();
    await expect(page.locator('[data-rule-enable]')).toHaveText('Disable rule');
  } finally {
    await closeServer(server);
  }
});

test('exports list opens detail with versions and unpublishes cleanly from detail view', async ({ page }) => {
  let exportStatus = 'ready';
  const buildExport = () => ({
    id: 'roads-public-2026',
    title: 'Regional Roads Export',
    status: exportStatus,
    currentVersion: 'v2',
    source: { kind: 'layer', serviceId: 'transport-roads', layerId: 1, name: 'Road Centerlines' },
    updatedAt: '2026-11-24T01:00:00Z',
    collectionUrl: 'https://gis.example.org/api/v1/exports/roads-public-2026/collection.json',
    publicUrl: exportStatus === 'unpublished' ? null : 'https://gis.example.org/public/roads-public-2026/collection.json',
    versions: [{
      version: 'v2',
      createdAt: '2026-11-24T01:00:00Z',
      featureCount: 4200,
      artifacts: [{ kind: 'geoparquet', href: 'https://gis.example.org/public/roads-public-2026/v2/roads.parquet', bytes: 2097152 }],
    }],
  });

  await mockPopulatedConsoleApis(page, staticOrigin);
  await page.route(`${staticOrigin}/api/v1/admin/exports`, route =>
    route.fulfill({ json: { success: true, data: { items: [buildExport()] } } }));
  await page.route(`${staticOrigin}/api/v1/admin/exports/roads-public-2026`, route => {
    if (route.request().method() === 'DELETE') exportStatus = 'unpublished';
    return route.fulfill({ json: { success: true, data: buildExport() } });
  });

  await signInWithFixturePassword(page, staticOrigin, '/operate/exports');
  const row = page.locator('[data-export-row="roads-public-2026"]');
  await expect(row).toContainText('Regional Roads Export');
  await row.getByRole('link', { name: 'Regional Roads Export', exact: true }).click();

  await page.waitForURL(url => url.pathname === '/console/operate/exports/roads-public-2026');
  await expect(page.locator('[data-export-version="v2"]')).toContainText('GeoParquet · 2 MB');
  await expect(page.locator('[data-export-access]')).toContainText('https://gis.example.org/public/roads-public-2026/collection.json');

  await page.locator('[data-export-unpublish="roads-public-2026"]').click();
  await page.locator('[data-console-confirm-accept]').click();
  await expect(page.locator('[data-export-unpublish="roads-public-2026"]')).toBeDisabled();
  await expect(page.locator('[data-export-access]')).not.toContainText('https://gis.example.org/public/roads-public-2026/collection.json');
});

async function assertFocusCyclesWithinOverlay(page: Page, overlaySelector: string, expectedCount: number) {
  const seen = new Set<string>();
  for (let step = 0; step < expectedCount + 2; step++) {
    const inside = await page.evaluate(selector => {
      const overlay = document.querySelector(selector);
      return Boolean(overlay && overlay.contains(document.activeElement));
    }, overlaySelector);
    expect(inside).toBe(true);
    const id = await page.evaluate(() => `${document.activeElement?.tagName}:${document.activeElement?.textContent?.trim()}`);
    seen.add(id);
    await page.keyboard.press('Tab');
  }
  expect(seen.size).toBeGreaterThanOrEqual(expectedCount);
}

test('rbac invite drawer moves focus on open, traps Tab, and restores trigger on Escape', async ({ page }) => {
  const fixture = await populatedMembersFixture(true);
  try {
    const address = fixture.server.address();
    if (!address || typeof address === 'string') throw new Error('Fixture did not bind TCP');
    await page.goto(`http://127.0.0.1:${address.port}/console/operate/access/members?environment=first`);
    const trigger = page.locator('[data-rbac-invite-open]');
    await expect(trigger).toBeVisible();
    await trigger.focus();
    await page.keyboard.press('Enter');

    const drawer = page.locator('[data-rbac-invite-drawer]');
    await expect(drawer).toBeVisible();
    const emailInput = drawer.locator('input[placeholder="name@example.gov"]');
    await expect(emailInput).toBeFocused();

    await page.keyboard.press('Shift+Tab');
    await expect(drawer.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(emailInput).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(drawer).toHaveCount(0);
    await expect(trigger).toBeFocused();
  } finally {
    await closeServer(fixture.server);
  }
});

test('import service replacement confirmation moves focus, traps Tab, and restores trigger on Escape', async ({ page }) => {
  await mockPopulatedConsoleApis(page, staticOrigin);
  await page.route(`${staticOrigin}/api/v1/admin/external-services/discover`, route => route.fulfill({
    json: {
      serviceName: 'Roads',
      serviceType: 'FeatureServer',
      normalizedUrl: 'https://source.example/FeatureServer',
      candidates: [{ layerId: 1, name: 'Roads' }],
    },
  }));

  await signInWithFixturePassword(page, staticOrigin, '/operate/import/service');
  await page.locator('input[placeholder^="https://"]').fill('https://source.example/FeatureServer');
  await page.getByRole('button', { name: 'Discover service', exact: true }).click();
  await expect(page.locator('[data-layer-checkbox]')).toBeVisible();

  await page.locator('[data-layer-checkbox]').check();
  await page.locator('[data-replace-target]').check();
  const trigger = page.locator('[data-import-selected]');
  await trigger.focus();
  await page.keyboard.press('Enter');

  const dialog = page.locator('[data-replace-confirmation]');
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('[data-cancel-replacement]')).toBeFocused();
  await assertFocusCyclesWithinOverlay(page, '[data-replace-confirmation]', 2);

  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(trigger).toBeFocused();
});
