import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import { startStaticBundleServer } from './static-bundle-server.mjs';
import { createContractApi } from './static-contract-server.mjs';

// Published-bundle fixture: the shipped config.json (auth.mode builtin) must still list
// server identity providers. Not installed-server acceptance.
const { chromium } = await import(process.env.HONUA_PLAYWRIGHT_MODULE ?? 'playwright');
const output = process.env.HONUA_BUILTIN_OIDC_OUTPUT ?? 'artifacts/builtin-oidc-signin';
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.HONUA_CHROMIUM_PATH
  ? { executablePath: process.env.HONUA_CHROMIUM_PATH, args: ['--no-sandbox'] } : {}) });
const cases = ['oidc', 'password'].flatMap(server => [false, true].flatMap(v4 =>
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
        const pathname = new URL(request.url, 'http://localhost').pathname;
        requests.push(`${request.method} ${pathname}`);
        // A password-only server still answers the anonymous provider list, with no providers.
        if (kind === 'password' && pathname === '/api/v1/admin/auth/config') {
          response.writeHead(200, { 'Content-Type': 'application/json' })
            .end(JSON.stringify({ oidcEnabled: false, providers: [] }));
          return;
        }
        await api(request, response);
      },
    });
  const origin = `http://127.0.0.1:${server.address().port}`;
  api = createContractApi({ allowedOrigin: origin, oidc: kind === 'oidc' });
  const close = async () => {
    server.closeAllConnections();
    await new Promise(resolve => server.close(resolve));
  };
  return { origin, requests, close };
}

async function walkJourney(page, { origin, requests, kind, name }) {
  await page.goto(`${origin}/console/`);
  const signIn = page.locator('[data-console-signin]');
  await signIn.waitFor();
  await page.screenshot({ path: `${output}/${name}-home.png`, fullPage: true });
  await signIn.click();
  await page.waitForURL(`${origin}/console/auth/signin`);
  await page.locator('input[type=password]').waitFor();
  await page.waitForFunction(() => !document.querySelector('[data-console-signin]'));
  const provider = page.getByRole('button', { name: 'Continue with Organization' });
  await page.waitForFunction(() => document.querySelector('form'));
  assert.ok(requests.includes('GET /api/v1/admin/auth/config'), 'Provider list was requested');
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
  await page.waitForURL(`${origin}/console/`);
  await page.getByText('Fixture Operator').first().waitFor();
  assert.equal(await page.locator('[data-console-signin]').count(), 0);
  await page.screenshot({ path: `${output}/${name}-signed-in.png`, fullPage: true });
}
