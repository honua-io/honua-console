import { expect, test, type Page } from '@playwright/test';

// #360 stripped the shared admin key from every privileged browser read: honua-server now
// only authorizes a real operator bearer. This smoke signs a dedicated operator in through
// honua-server's own OIDC IdP and lets the Console exchange that session for its operator
// bearer, the same governed path proved by the honua-release console-read-approve receipt
// harness (certification/console-read-approve/browser.mjs). It never restores the admin key.
const OPERATOR_USERNAME = process.env.HONUA_CONSOLE_AWS_OPERATOR_USERNAME;
const OPERATOR_PASSWORD = process.env.HONUA_CONSOLE_AWS_OPERATOR_PASSWORD;

async function submitIdpCredentials(page: Page): Promise<void> {
  // The realm login pages exercised elsewhere in this repo (the honua-release receipt
  // harness, e2e/playwright/live-auth) are Keycloak-shaped. Try that form first and fall
  // back to accessible locators for a differently-shaped provider on the AWS demo IdP.
  const keycloakUsername = page.locator('#username');
  if (await keycloakUsername.isVisible().catch(() => false)) {
    await keycloakUsername.fill(OPERATOR_USERNAME!);
    await page.locator('#password').fill(OPERATOR_PASSWORD!);
    await page.locator('#kc-login').click();
    return;
  }

  await page.getByLabel(/username|email/i).first().fill(OPERATOR_USERNAME!);
  await page.getByLabel(/password/i).first().fill(OPERATOR_PASSWORD!);
  await page.getByRole('button', { name: /sign in|log in|continue/i }).first().click();
}

// Begins the Console's server-delegated sign-in (docs/console-authentication.md,
// "Operator bearer exchange and deployment topology"): the operator authenticates against
// honua-server's real IdP, the IdP returns to the Console-origin callback, and the Console
// exchanges the resulting server session for its own operator bearer. `profileId=local-dev`
// is the environment profile the Console auto-seeds from HONUA_SERVER_BASE_URL in
// Development (see BuildBrowserDevSeed in src/Honua.Console.Web/Auth/ConsoleAuthentication.cs).
async function signInAsOperator(page: Page, baseURL: string, returnTo: string): Promise<void> {
  if (!OPERATOR_USERNAME || !OPERATOR_PASSWORD) {
    throw new Error(
      'HONUA_CONSOLE_AWS_OPERATOR_USERNAME and HONUA_CONSOLE_AWS_OPERATOR_PASSWORD are required to sign the smoke operator in',
    );
  }

  await page.goto(`/auth/server/login?profileId=local-dev&returnTo=${encodeURIComponent(returnTo)}`);
  await page.waitForURL((url) => url.origin !== baseURL, { timeout: 60_000 });
  await submitIdpCredentials(page);
  await page.waitForURL(`${baseURL}${returnTo}`, { timeout: 60_000 });
}

test('Console boots against AWS as a governed operator and renders the live resource/publication tree', async ({ page, baseURL }, testInfo) => {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') errors.push(`console: ${message.text()}`);
  });
  page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));

  await test.step('fails closed before the operator signs in', async () => {
    const response = await page.goto('/operate/data', { waitUntil: 'domcontentloaded' });
    expect(response?.status()).toBe(200);
    // Data & Layers renders per-capability cards (OperateCapabilityStateList.razor), not the
    // top-level .console-state-error surface used by Catalog below.
    const missingPermission = page.locator('.console-panel', { hasText: '401' });
    await expect(missingPermission.first()).toBeVisible();
  });

  await test.step('the smoke operator signs in through the real honua-server IdP', async () => {
    await signInAsOperator(page, baseURL!, '/operate/data');
  });

  const response = await page.goto('/operate/data', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Data & Layers', exact: true })).toBeVisible();
  await expect(page.locator('[data-operate-data]')).toBeVisible();

  const resources = page.locator('[data-resource-node]');
  await expect(resources.first()).toBeVisible();
  expect(await resources.count(), 'the AWS-bound Console should render live resources').toBeGreaterThan(0);
  await expect(page.locator('.console-state-error')).toHaveCount(0);

  // The server prerenders this page before the Blazor circuit is interactive.
  // Prove an input event has reached the live circuit before clicking the tree;
  // otherwise a fast browser can click inert prerendered markup.
  const filter = page.getByRole('searchbox', { name: 'Filter resources' });
  await expect(async () => {
    await filter.fill('__console_interactivity_probe__');
    await expect(resources).toHaveCount(0, { timeout: 1_000 });
  }).toPass({ timeout: 20_000 });
  await filter.fill('');
  await expect(resources.first()).toBeVisible();

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

test('Console catalog resolves against the same AWS environment for the signed-in operator', async ({ page, baseURL }) => {
  await signInAsOperator(page, baseURL!, '/catalog');

  const response = await page.goto('/catalog', { waitUntil: 'domcontentloaded' });
  expect(response?.status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'Catalog', exact: true }).first()).toBeVisible();
  await expect(page.locator('.console-state-error')).toHaveCount(0);

  const tableRows = page.locator('.console-content-table tbody tr');
  const serverBridge = page.locator('[data-catalog-server-bridge]');
  const emptyState = page.locator('.console-state-empty');
  await expect
    .poll(async () => (await tableRows.count()) + (await serverBridge.count()) + (await emptyState.count()))
    .toBeGreaterThan(0);
});
