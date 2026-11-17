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
const cases = [false, true].flatMap(v4 => [1280, 390].flatMap(width =>
  ['500', 'drop'].flatMap(failure => ['badge', 'home'].map(surface => ({ v4, width, failure, surface })))));
try {
  for (const scenario of cases) await checkRecovery(scenario);
} finally {
  await browser.close();
  await fs.writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
}
assert.equal(results.filter(row => !row.passed).length, 0, 'Every approval recovery case must pass');

async function startFixture(scenario) {
  const state = { recovering: false, count: 12, reads: 0 };
  let api;
  const server = await startStaticBundleServer(
    process.env.HONUA_CONSOLE_STATIC_ROOT ?? 'artifacts/approval-recovery-publish/wwwroot/console', 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: scenario.v4 },
      handleApi: async (request, response) => {
        const pathname = new URL(request.url, 'http://localhost').pathname;
        if (pathname === '/api/v1/admin/proposals') return respondProposals({ response, state, scenario });
        // Keep realtime unavailable so recovery cannot depend on an event or navigation.
        if (pathname === '/api/v1/admin/live') { response.writeHead(503).end(); return; }
        await api(request, response);
      },
    });
  const origin = `http://127.0.0.1:${server.address().port}`;
  api = createContractApi({ allowedOrigin: origin });
  return { server, origin, state };
}

function respondProposals({ response, state, scenario }) {
  state.reads++;
  if (!state.recovering) {
    if (scenario.failure === 'drop') response.destroy();
    else response.writeHead(500).end();
    return;
  }
  const proposals = Array.from({ length: state.count }, (_, index) => ({
    proposalId: `fixture-${index}`, kind: 'UpdateService', status: 'AwaitingApproval',
    requestedBy: 'fixture-operator', summary: `Pending fixture ${index}`, riskLevel: 'Low',
    createdAt: '2026-11-17T00:00:00Z', updatedAt: '2026-11-17T00:00:00Z',
  }));
  response.writeHead(200, { 'Content-Type': 'application/json' }).end(JSON.stringify({ proposals }));
}

async function checkRecovery(scenario) {
  const fixture = await startFixture(scenario);
  const context = await browser.newContext({ viewport: { width: scenario.width, height: 900 } });
  await context.addCookies([{ name: 'console-fixture-session', value: 'issued', url: fixture.origin }]);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const row = { ...scenario, passed: false };
  results.push(row);
  try {
    await exerciseRecovery({ page, fixture, row });
    assert.deepEqual(errors, []);
    row.totalReads = fixture.state.reads;
    row.passed = true;
  } catch (error) { row.error = error.stack; }
  finally {
    row.pageErrors = errors;
    await context.close();
    fixture.server.closeAllConnections();
    await new Promise(resolve => fixture.server.close(resolve));
  }
  console.log(JSON.stringify(row));
}

async function exerciseRecovery({ page, fixture, row }) {
  const route = row.surface === 'home' ? '/console/' : '/console/operate/observability';
  await page.goto(`${fixture.origin}${route}`);
  await page.getByRole('heading', { level: 1 }).first().waitFor();
  const selector = row.surface === 'home' ? '[data-home-awaiting-count]' : '.console-approval-count';
  if (row.surface === 'badge') await openNavigation(page);
  await assertEventually(() => fixture.state.reads > 0, 'Initial failed read was exercised');
  if (row.surface === 'home') await page.locator('[data-home-inbox-error]').waitFor();
  assert.equal(await page.locator(selector).count(), 0, 'Failed reads must not display a zero count');
  row.failedReads = fixture.state.reads;
  const prefix = `${output}/${row.v4}-${row.width}-${row.failure}-${row.surface}`;
  await page.screenshot({ path: `${prefix}-unavailable.png`, fullPage: true });
  const started = Date.now();
  fixture.state.recovering = true;
  await page.locator(selector).filter({ hasText: /\b12\b/ }).first().waitFor({ timeout: 35_000 });
  row.recoveryMs = Date.now() - started;
  fixture.state.count = 13;
  await page.locator(selector).filter({ hasText: /\b13\b/ }).first().waitFor({ timeout: 35_000 });
  assert.equal(new URL(page.url()).pathname, route);
  await page.screenshot({ path: `${prefix}-recovered.png`, fullPage: true });
}

async function openNavigation(page) {
  const toggle = page.locator('[data-navigation-toggle]');
  if (await toggle.isVisible()) await toggle.click();
}

async function assertEventually(predicate, message) {
  for (let attempt = 0; attempt < 100; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.fail(message);
}
