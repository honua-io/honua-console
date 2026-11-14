import { expect, test } from '@playwright/test';
import { fileURLToPath } from 'node:url';
import { startStaticBundleServer } from '../static-bundle-server.mjs';
import { createContractApi } from '../static-contract-server.mjs';

const origin = 'http://127.0.0.1:5196';
const prefix = '/api/v1/admin/observability/alerts';
const initial = { eventId: 42, ruleId: 42, ruleName: 'A map needs attention',
  serviceId: 'Public maps', severity: 'warning', lifecycleStatus: 'open', resourceRef: 'map/42' };

async function startLifecycleFixture() {
  const fallback = createContractApi({ allowedOrigin: origin });
  const attempts: { path: string; id: string; deadline?: string }[] = [];
  let alert = { ...initial, suppressedUntil: undefined as string | undefined,
    resolvedAt: undefined as string | undefined };
  const server = await startStaticBundleServer(
    fileURLToPath(new URL('../../../artifacts/static-publish/wwwroot/console', import.meta.url)), 5196, {
    configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: true },
    handleApi: async (req, res) => {
      const url = new URL(req.url, origin);
      if (url.pathname !== prefix && !url.pathname.startsWith(`${prefix}/42/`)) return fallback(req, res);
      if (!req.headers.cookie?.includes('console-fixture-session=issued')) { res.writeHead(401).end(); return; }
      if (req.method === 'POST') {
        let raw = '';
        for await (const chunk of req) raw += chunk;
        const body = raw ? JSON.parse(raw) : {};
        attempts.push({ path: url.pathname, id: req.headers['idempotency-key'], deadline: body.suppressUntil });
        if (url.pathname.endsWith('/suppress')) {
          if (attempts.length === 1) { res.writeHead(503).end(); return; }
          alert = { ...alert, suppressedUntil: body.suppressUntil };
        } else if (url.pathname.endsWith('/resolve')) {
          alert = { ...alert, resolvedAt: new Date().toISOString(), lifecycleStatus: 'resolved' };
        }
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(url.pathname === prefix ? { items: [alert], nextCursor: null } : alert));
    },
  });
  return { server, attempts };
}

test('inbox retains snooze retry identity and confirms resolution', async ({ page }, testInfo) => {
  testInfo.annotations.push({ type: 'contract-fixture', description:
    'Published Console with authenticated alert contracts; live-server qualification is separate.' });
  const { server, attempts } = await startLifecycleFixture();
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  try {
    await page.goto(`${origin}/console/auth/signin?returnTo=%2Finbox`);
    await page.getByLabel('Password', { exact: true }).fill('fixture-password');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    const panel = page.locator('[data-operations-alerts]');
    await expect(panel.getByRole('heading', { name: initial.ruleName })).toBeVisible();
    await panel.locator('[data-snooze-alert]').click();
    await expect(panel.locator('[data-alert-action-message]')).toContainText('could not complete');
    await expect(panel.locator('[data-snoozed]')).toHaveCount(0);
    await panel.locator('[data-snooze-alert]').click();
    await expect(panel.locator('[data-snoozed]')).toHaveCount(1);
    expect(attempts).toHaveLength(2);
    expect(attempts[0].id).toBeTruthy();
    expect(attempts[1]).toEqual(attempts[0]);
    expect(Number.isFinite(Date.parse(attempts[0].deadline!))).toBe(true);
    await testInfo.attach('operations-snoozed', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    await panel.locator('[data-resolve-alert]').click();
    await expect(panel.locator('[data-alert-action-message]')).toContainText('does not confirm');
    await expect(panel.locator('[data-resolve-alert]')).toHaveCount(0);
    await expect(panel.locator('[data-snooze-alert]')).toHaveCount(0);
    expect(attempts).toHaveLength(3);
    expect(attempts[2].id).toBeTruthy();
    expect(attempts[2].id).not.toBe(attempts[1].id);
    await testInfo.attach('operations-resolved', {
      body: await page.screenshot({ fullPage: true }), contentType: 'image/png',
    });
    expect(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)).toBe(false);
    expect(errors).toEqual([]);
  } finally {
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
  }
});
