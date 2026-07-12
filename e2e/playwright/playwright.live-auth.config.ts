import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

// Live shared-origin BFF auth proof (honua-console#303 / PR #305 deploy-proof lane).
//
// Drives the STOCK Console Web host against a REAL honua-server whose operator OIDC is a
// REAL IdP (Keycloak), in the documented shared-origin callback topology
// (docs/console-authentication.md "Operator bearer exchange and deployment topology"):
//   - honua-server PUBLIC_BASE_URL -> the Console origin, so the server-built OIDC
//     redirect_uri is http://127.0.0.1:5274/admin/auth/callback — served BY the Console's
//     operator-partitioned session BFF;
//   - the IdP client registers ONLY that shared-origin redirect URI;
//   - the server API stays on its own internal origin (http://127.0.0.1:5281).
//
// Prerequisites it does NOT boot (provide them first):
//   cd e2e/playwright/live-auth && ./gen-certs.sh && docker compose up -d --wait
// Playwright boots ONLY the stock Console host (no admin key, so nothing the Console
// renders from admin surfaces can come from anywhere but the exchanged operator bearer).
//
// Run:  npx playwright test --config playwright.live-auth.config.ts

const PORT = Number(process.env.HONUA_CONSOLE_E2E_LIVE_AUTH_PORT ?? '5274');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_E2E_LIVE_AUTH_SERVER_URL ?? 'http://127.0.0.1:5281';
// fileURLToPath keeps the cwd valid on Windows too (URL.pathname yields /C:/...).
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

export default defineConfig({
  testDir: './live-auth/specs',
  // One Console process, one flow ordering: the specs build on prior operator state.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  reporter: [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // Keycloak serves the self-signed cert from live-auth/certs.
    ignoreHTTPSErrors: true,
    launchOptions: {
      // host.docker.internal resolves to the LAN adapter on this host, where Docker's
      // published port may be firewalled; pin it to loopback for the browser so the
      // IdP hostname is the SAME one the honua-server container uses (issuer parity).
      args: ['--host-resolver-rules=MAP host.docker.internal 127.0.0.1'],
    },
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      // Stock browser host in Development: dev seed profile (AccountRbac ->
      // HONUA_SERVER_BASE_URL) + dev cookie login for operator A. NO admin API key:
      // admin reads can only succeed over the exchanged operator bearer.
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      HONUA_SERVER_BASE_URL: SERVER_URL,
      // Trusted-edge lane for the SECOND operator (distinct partition key) so the
      // cross-operator isolation checks run against the live flow. The shared secret
      // means requests without the header are untouched (operator A's dev cookie).
      Honua__Console__Auth__EdgeForwarded__Enabled: 'true',
      Honua__Console__Auth__EdgeForwarded__SharedSecret: 'live-proof-edge-secret',
    },
  },
});
