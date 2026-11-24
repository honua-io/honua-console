import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { startStaticBundleServer } from './static-bundle-server.mjs';
import { createContractApi } from './static-contract-server.mjs';

// Published-bundle fixture: the shipped config.json (auth.mode builtin) must still list
// server identity providers, and a signed-out visit must reach sign-in without the menu or
// privileged requests. Not installed-server acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const output = process.env.HONUA_BUILTIN_OIDC_OUTPUT ?? 'artifacts/builtin-oidc-signin';
const settingsRoute = '/operate/settings';
const homePath = '/console/';
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const cases = ['oidc', 'oidc-legacy', 'password'].flatMap(server => [false, true].flatMap(v4 =>
  [1280, 390].map(width => ({ server, v4, width }))));
const results = [];
try {
  for (const testCase of cases) results.push(await runCase(testCase));
} finally {
  await browser.close();
  await fs.writeFile(`${output}/results.json`, JSON.stringify(results, null, 2));
}
assert.equal(results.filter(row => !row.passed).length, 0, 'Every built-in sign-in case must pass');

async function runCase({ server: kind, v4, width }) {
  const fixture = await startFixture(kind, v4);
  const context = await browser.newContext({ viewport: { width, height: 900 } });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));
  const row = { server: kind, v4, width, passed: false };
  try {
    await walkJourney(page, { ...fixture, kind, name: `${kind}-${v4 ? 'v4' : 'legacy'}-${width}` });
    assert.deepEqual(errors, []);
    row.passed = true;
  } catch (error) { row.error = error.stack; }
  finally {
    Object.assign(row, { pageErrors: errors, requests: fixture.requests });
    await context.close();
    await fixture.close();
  }
  console.log(JSON.stringify({ ...row, requests: undefined }));
  return row;
}

async function startFixture(kind, v4) {
  const requests = [];
  let api;
  const server = await startStaticBundleServer(
    process.env.HONUA_CONSOLE_STATIC_ROOT ?? 'artifacts/static-publish/wwwroot/console', 0, {
      configuration: { apiBaseUrl: '/', auth: { mode: 'builtin' }, v4Enabled: v4 },
      handleApi: async (request, response) => {
        requests.push(`${request.method} ${new URL(request.url, 'http://localhost').pathname}`);
        await api(request, response);
      },
    });
  const origin = `http://127.0.0.1:${server.address().port}`;
  api = createContractApi({
    allowedOrigin: origin,
    oidc: kind !== 'password',
    oidcReturnPaths: kind === 'oidc' ? [homePath, `/console${settingsRoute}`] : undefined,
  });
  const close = async () => {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  };
  return { origin, requests, close };
}

async function walkJourney(page, { origin, requests, kind, name }) {
  // Signed out, a protected route goes straight to sign-in: no menu, tree or inventory, and no API
  // request beyond the auth endpoints and the public capability manifest.
  const requestedRoute = kind === 'password' ? '/operate/connections' : settingsRoute;
  await page.goto(`${origin}/console${requestedRoute}`);
  await page.waitForURL(`${origin}/console/auth/signin?returnTo=${encodeURIComponent(requestedRoute)}`);
  await page.locator('input[type=password]').waitFor();
  await page.locator('[data-console-signed-out]').waitFor();
  for (const selector of ['.console-topnav', '[data-publication-navigation]', '.console-contextbar', '[data-console-signin]']) {
    assert.equal(await page.locator(selector).count(), 0, `${selector} must not render signed out`);
  }
  const provider = page.getByRole('button', { name: 'Continue with Organization' });
  await page.waitForFunction(() => document.querySelector('form'));
  assert.ok(requests.includes('GET /api/v1/admin/auth/config'), 'Provider list was requested');
  const privileged = requests.filter(entry => entry.includes('/api/')
    && !entry.includes('/api/v1/admin/auth/') && !entry.endsWith(' /api/v1/capabilities/manifest'));
  assert.deepEqual(privileged, [], 'No privileged API request before sign-in');
  if (kind === 'password') {
    assert.equal(await page.locator('[data-signin-divider]').count(), 0);
    assert.equal(await provider.count(), 0);
    assert.equal(await page.locator('form [role=status], [role=alert]').count(), 0);
    await page.screenshot({ path: `${output}/${name}-signin.png`, fullPage: true });
    return;
  }
  await provider.waitFor();
  await page.screenshot({ path: `${output}/${name}-signin.png`, fullPage: true });
  await provider.click();
  const expectedPath = kind === 'oidc-legacy' ? homePath : `/console${settingsRoute}`;
  await page.waitForURL(`${origin}${expectedPath}`);
  const authorizeRequests = requests.filter(entry => entry === 'POST /api/v1/admin/auth/providers/entra/authorize-url');
  assert.equal(authorizeRequests.length, kind === 'oidc-legacy' ? 2 : 1);
  await page.locator('.console-account-identity[title="Fixture Operator"]').first().waitFor({ state: 'attached' });
  await page.locator('.console-topnav').waitFor({ state: 'attached' });
  if (kind === 'oidc') {
    await page.getByRole('heading', { name: 'Settings', exact: true, level: 1 }).waitFor();
  }
  assert.equal(await page.locator('[data-console-signed-out]').count(), 0);
  await page.screenshot({ path: `${output}/${name}-signed-in.png`, fullPage: true });
}
