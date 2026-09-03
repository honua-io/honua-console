import { test, expect } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  FOCUSED_RECEIPT_SCHEMA, focusedResourceIdentities, loadTerminalReceipt, writeFocusedEvidence,
  type FocusedClientEvidence,
} from '../receipt';

const receiptPath = process.env.HONUA_CONSOLE_FOCUSED_TERMINAL_RECEIPT;
const outputPath = process.env.HONUA_CONSOLE_FOCUSED_EVIDENCE_PATH
  ?? path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'evidence', 'console-focused-client.json');
const idpUser = process.env.HONUA_CONSOLE_FOCUSED_IDP_USER;
const idpPassword = process.env.HONUA_CONSOLE_FOCUSED_IDP_PASSWORD;

function successSurface(page: import('@playwright/test').Page, kind: FocusedClientEvidence['inspected'][number]['kind']) {
  switch (kind) {
    case 'connection': return page.locator('.console-page-heading h1');
    case 'service': return page.locator('[data-service-detail]');
    case 'layer': return page.locator('[data-layer-preview]');
    case 'gpJob':
    case 'gpResult': return page.locator('#job-detail-heading');
    case 'savedMap':
    case 'savedDashboard': return page.locator('h2').filter({ hasText: 'Versions' });
    case 'proposal': return page.locator('[data-proposal-id]');
    case 'operation': return page.locator('#deploy-approval-heading');
    case 'audit': return page.locator('#event-detail-heading');
    case 'publication': return page.locator('.publish-review-stack');
  }
}

async function expectLoadedResource(page: import('@playwright/test').Page, identity: ReturnType<typeof focusedResourceIdentities>[number]) {
  // Several missing/forbidden surfaces echo the requested id in their explanatory copy. The
  // explicit success surface must be present before this identity can be recorded as passed.
  await expect(page.locator('body')).not.toContainText(/\b(?:not found|forbidden|access denied|permission denied)\b/i);
  await expect(successSurface(page, identity.kind)).toBeVisible();
  if (identity.kind === 'service') {
    await expect(successSurface(page, identity.kind)).toHaveAttribute('data-service-detail', identity.displayValue ?? identity.id);
  } else {
    await expect(page.locator('body')).toContainText(identity.id);
  }
}

test('inspects every exact receipt identity and emits independent UI evidence', async ({ page }) => {
  test.skip(!receiptPath, 'HONUA_CONSOLE_FOCUSED_TERMINAL_RECEIPT is not configured');
  test.skip(!idpUser || !idpPassword, 'focused candidate principal is not configured');

  const receipt = loadTerminalReceipt(receiptPath!);
  const identities = focusedResourceIdentities(receipt);
  expect(identities.length, 'terminal receipt must expose at least one focused identity').toBeGreaterThan(0);

  // Stock Development login establishes the Console operator. The server BFF then exchanges
  // that operator through the configured real IdP. The focused config deliberately supplies no
  // shared admin key, so every successful read below is necessarily operator-bearer backed.
  await page.goto('/auth/login');
  await page.waitForURL('**/');
  await page.goto(`/auth/server/login?profileId=local-dev&returnTo=${encodeURIComponent('/operate/health')}`);
  await page.locator('#username').fill(idpUser!);
  await page.locator('#password').fill(idpPassword!);
  await page.locator('#kc-login').click();
  await page.waitForURL('**/operate/health');

  const inspected: FocusedClientEvidence['inspected'] = [];
  for (const identity of identities) {
    await page.goto(identity.route);
    await expect(page.locator('h1')).toBeVisible();
    await expectLoadedResource(page, identity);
    if (identity.versionId) await expect(page.locator('body')).toContainText(identity.versionId);
    if (identity.contentHash) await expect(page.locator('body')).toContainText(identity.contentHash);
    inspected.push({ kind: identity.kind, id: identity.id, route: identity.route, status: 'pass' });
  }

  // Health/diagnostic and recovery views are read-only and can be certified before approval exists.
  for (const route of ['/operate/health', '/operate/releases', '/operate/observability', '/support']) {
    await page.goto(route);
    await expect(page.locator('h1')).toBeVisible();
  }

  const evidence: FocusedClientEvidence = {
    schema: FOCUSED_RECEIPT_SCHEMA,
    evidenceKey: 'console.focused-client',
    generatedAt: new Date().toISOString(),
    terminalReceipt: { path: path.basename(receiptPath!), evidenceKey: receipt.evidenceKey, status: receipt.status },
    server: receipt.server ?? {},
    inspected,
    approval: { status: 'blocked', blockedBy: 'honua-server#3365' },
    status: 'pass',
  };
  writeFocusedEvidence(outputPath, evidence);
});
