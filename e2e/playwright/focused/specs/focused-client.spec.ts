import { test, expect } from '@playwright/test';
import path from 'node:path';
import {
  FOCUSED_RECEIPT_SCHEMA, focusedResourceIdentities, loadTerminalReceipt, writeFocusedEvidence,
  type FocusedClientEvidence,
} from '../receipt';

const receiptPath = process.env.HONUA_CONSOLE_FOCUSED_TERMINAL_RECEIPT;
const outputPath = process.env.HONUA_CONSOLE_FOCUSED_EVIDENCE_PATH ?? 'e2e/playwright/focused/evidence/console-focused-client.json';
const idpUser = process.env.HONUA_CONSOLE_FOCUSED_IDP_USER;
const idpPassword = process.env.HONUA_CONSOLE_FOCUSED_IDP_PASSWORD;

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
    await expect(page.locator('body')).toContainText(identity.id);
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
