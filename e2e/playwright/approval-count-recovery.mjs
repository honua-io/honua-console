import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { startStaticBundleServer } from './static-bundle-server.mjs';
import { createContractApi } from './static-contract-server.mjs';

// Published-source fixture: tests browser recovery, not installed server acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const output = process.env.HONUA_APPROVAL_RECOVERY_OUTPUT ?? 'artifacts/approval-count-recovery';
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const results = [];
try {
  for (const v4 of [false, true]) for (const width of [1280, 390]) for (const failure of ['500', 'drop']) {
    let recovering = false;
    let count = 12;
    let reads = 0;
    let api;
    const server = await startStaticBundleServer(
      process.env.HONUA_CONSOLE_STATIC_ROOT ?? 'artifacts/approval-recovery-publish/wwwroot/console', 0, {
        configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: v4 },
        handleApi: async (request, response) => {
          const pathname = new URL(request.url, 'http://localhost').pathname;
          if (pathname === '/api/v1/admin/proposals') {
            reads++;
            if (!recovering) {
              if (failure === 'drop') response.destroy();
              else response.writeHead(500).end();
              return;
            }
            const proposals = Array.from({ length: count }, (_, index) => ({
              proposalId: `fixture-${index}`, kind: 'UpdateService', status: 'AwaitingApproval',
              requestedBy: 'fixture-operator', summary: `Pending fixture ${index}`, riskLevel: 'Low',
              createdAt: '2026-11-17T00:00:00Z', updatedAt: '2026-11-17T00:00:00Z',
            }));
            response.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ proposals }));
            return;
          }
          // Keep realtime unavailable: the badge must recover even without an event or navigation.
          if (pathname === '/api/v1/admin/live') { response.writeHead(503).end(); return; }
          await api(request, response);
        },
      });
    const origin = `http://127.0.0.1:${server.address().port}`;
    api = createContractApi({ allowedOrigin: origin });
    const context = await browser.newContext({ viewport: { width, height: 900 } });
    await context.addCookies([{ name: 'console-fixture-session', value: 'issued', url: origin }]);
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const row = { v4, width, failure, passed: false };
    results.push(row);
    try {
      await page.goto(`${origin}/console/operate/observability`);
      await page.getByRole('heading', { level: 1, name: v4 ? 'Activity' : 'Operate Observability', exact: true }).waitFor();
      const toggle = page.locator('[data-navigation-toggle]');
      if (await toggle.isVisible()) await toggle.click();
      await page.waitForFunction(() => !document.querySelector('.console-approval-count'));
      await assertEventually(() => reads > 0, 'Initial failed read was exercised');
      row.failedReads = reads;
      await page.screenshot({ path: `${output}/${v4}-${width}-${failure}-unavailable.png`, fullPage: true });
      const started = Date.now();
      recovering = true;
      await page.locator('.console-approval-count').filter({ hasText: /^12$/ }).first().waitFor({ timeout: 35_000 });
      row.recoveryMs = Date.now() - started;
      assert.equal(await page.locator('.console-approval-count').first().getAttribute('aria-label'), '12 awaiting approval');
      // A proposal missed while the stream is in fallback must also appear without interaction.
      count = 13;
      await page.locator('.console-approval-count').filter({ hasText: /^13$/ }).first().waitFor({ timeout: 35_000 });
      assert.equal(new URL(page.url()).pathname, '/console/operate/observability');
      assert.deepEqual(errors, []);
      row.totalReads = reads;
      row.passed = true;
      await page.screenshot({ path: `${output}/${v4}-${width}-${failure}-recovered.png`, fullPage: true });
    } catch (error) { row.error = error.stack; }
    finally {
      row.pageErrors = errors;
      await context.close();
      server.closeAllConnections();
      await new Promise(resolve => server.close(resolve));
    }
    console.log(JSON.stringify(row));
  }
} finally {
  await browser.close();
  await fs.writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
}
assert.equal(results.filter(row => !row.passed).length, 0, 'Every approval recovery case must pass');

async function assertEventually(predicate, message) {
  for (let attempt = 0; attempt < 100; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.fail(message);
}
