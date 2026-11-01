import { randomUUID } from 'node:crypto';
import { expect, request, test } from '@playwright/test';
import { establishGovernedOperatorSession } from '../operator-session';
import { sourceConnectionBody } from '../source-db';

test('connection selection is unavailable until its interactive handler is ready', async ({ page }) => {
  let connectionId: string;
  const verifier = await request.newContext({
    baseURL: process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088',
    extraHTTPHeaders: { 'X-API-Key': process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key' },
  });
  try {
    const created = await verifier.post('/api/v1/admin/connections/', {
      data: sourceConnectionBody(`hydrate-${randomUUID().replaceAll('-', '').slice(0, 16)}`),
    });
    expect(created.status()).toBe(201);
    connectionId = (await created.json()).data.connectionId;
    const tables = await verifier.get(`/api/v1/admin/connections/${connectionId}/tables`);
    expect(tables.ok()).toBe(true);
    expect((await tables.json()).tables.length).toBeGreaterThan(0);
  } finally { await verifier.dispose(); }
  await establishGovernedOperatorSession(page);

  let release!: () => void;
  let blocked = false;
  const allowed = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/_framework/blazor.web*.js*', async route => {
    blocked = true;
    await allowed;
    await route.continue();
  });
  try {
    await page.goto('/operate/publishing/quick', { waitUntil: 'commit' });
    await expect.poll(() => blocked).toBe(true);
    const connection = page.getByLabel('Connection', { exact: true });
    await expect(connection).toBeVisible();
    await expect(connection).toBeDisabled();
    release();
    await expect(connection).toBeEnabled();
    await connection.selectOption(connectionId);
    await expect(page.getByLabel('Table', { exact: true })).toBeEnabled();
  } finally { release(); }
});
