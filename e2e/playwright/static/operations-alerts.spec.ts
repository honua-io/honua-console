import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';

const origin = 'http://127.0.0.1:5195';
const prefix = '/api/v1/admin/observability/alerts';
const alert = { eventId: 42, ruleId: 42, ruleName: 'A map needs attention',
  serviceId: 'Public maps', severity: 'warning', lifecycleStatus: 'open', resourceRef: 'map/42' };

async function startInboxFixture() {
  const fallback = createContractApi({ allowedOrigin: origin });
  const requestIds: string[] = [];
  let acknowledged = false;
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 5195, {
    configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
    handleApi: async (req, res) => {
      const url = new URL(req.url, origin);
      if (url.pathname !== prefix && url.pathname !== `${prefix}/42/acknowledge`) {
        return fallback(req, res);
      }
      if (!req.headers.cookie?.includes('console-fixture-session=issued')) {
        res.writeHead(401).end();
        return;
      }
      if (req.method === 'POST') {
        requestIds.push(req.headers['idempotency-key']);
        if (requestIds.length === 1) { res.writeHead(503).end(); return; }
        acknowledged = true;
      }
      const current = { ...alert, ...(acknowledged ? { acknowledgedAt: '2026-11-14T00:00:00Z' } : {}) };
      const body = req.method === 'POST' ? current : {
        items: [{ ...current, eventId: url.searchParams.has('cursor') ? 43 : 42 }],
        nextCursor: url.searchParams.has('cursor') ? null : 'older+/=cursor',
      };
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(body));
    },
  });
  return { server, requestIds };
}

test('inbox confirms acknowledgment, retains retry identity and follows pagination', async ({ page }, testInfo) => {
  testInfo.annotations.push({ type: 'contract-fixture', description:
    'Published Console with authenticated alert contracts; live-server qualification is separate.' });
  const { server, requestIds } = await startInboxFixture();
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto(`${origin}/console/auth/signin?returnTo=%2Finbox`);
    await page.getByLabel('Password', { exact: true }).fill('fixture-password');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    const panel = page.locator('[data-operations-alerts]');
    await expect(panel.getByRole('heading', { name: alert.ruleName })).toBeVisible();
    await panel.locator('[data-acknowledge-alert]').click();
    await expect(panel.locator('[data-alert-action-message]')).toContainText('could not complete');
    await expect(panel.locator('[data-acknowledged]')).toHaveCount(0);
    await panel.locator('[data-acknowledge-alert]').click();
    await expect(panel.locator('[data-acknowledged]')).toHaveCount(1);
    expect(requestIds).toHaveLength(2);
    expect(requestIds[0]).toBeTruthy();
    expect(requestIds[1]).toBe(requestIds[0]);
    await testInfo.attach('operations-inbox', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await panel.locator('[data-next-alerts]').click();
    await expect(panel.locator('[data-alert-id="43"]')).toHaveCount(1);
    await expect(panel.locator('[data-next-alerts]')).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    expect(errors).toEqual([]);
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});
