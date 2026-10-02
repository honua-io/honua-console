import { defineConfig, devices } from '@playwright/test';

// Live-backed real-browser e2e for the Honua Console.
//
// Unlike playwright.config.ts (which runs the host strictly backend-free for missing-binding
// smoke), this config boots the Console BOUND to a real honua-server and drives create/test
// flows that mutate live server state, verifying the result through the server admin API.
//
// The Console host strips X-API-Key. Privileged browser calls need a server-bound operator
// bearer from the governed OIDC exchange (live/operator-session.ts). The admin key below is
// read only by live/admin-api.ts, which calls honua-server directly as an independent
// verifier. It is deliberately NOT given to the Console process.
//
// Prerequisites it does NOT boot (the release harness must provide this topology):
//   - honua-server at HONUA_CONSOLE_E2E_SERVER_URL (default http://127.0.0.1:8088).
//   - HONUA_CONSOLE_E2E_ADMIN_KEY (default honua-console-dev-key) for the verifier only.
//   - A local OIDC IdP at HONUA_CONSOLE_E2E_IDP_HOST (default host.docker.internal:8443)
//     whose client redirect URI is
//     http://127.0.0.1:${HONUA_CONSOLE_E2E_LIVE_PORT}/admin/auth/callback
//     and whose operator user is HONUA_CONSOLE_E2E_OPERATOR_USER / _PASSWORD
//     (defaults alice / alice-live-proof-pw, realm role admin, `roles` claim).
//   - That server's Public:BaseUrl AND PUBLIC_BASE_URL set to this Console origin
//     (http://127.0.0.1:${HONUA_CONSOLE_E2E_LIVE_PORT}, default port 5176), with
//     Oidc:Generic enabled against that IdP, Authentication:OperatorBearer enabled,
//     RateLimiting disabled, and the IdP TLS cert trusted for the server's backchannel.
//     Use a server dedicated to this suite. The slice-1 stack's public base URL also
//     builds STAC/OGC links, so it cannot be retargeted at the Console.
//   - Source database inputs (HONUA_CONSOLE_E2E_SOURCE_*) and HONUA_TEST_DB_DSN inside
//     the server, as before.
// Playwright boots ONLY the Console on HONUA_CONSOLE_E2E_LIVE_PORT.

const IDP_HOST = process.env.HONUA_CONSOLE_E2E_IDP_HOST ?? 'host.docker.internal:8443';
const IDP_HOSTNAME = IDP_HOST.replace(/:\d+$/, '');
const MAP_IDP_TO_LOOPBACK = IDP_HOSTNAME === 'host.docker.internal'
  || process.env.HONUA_CONSOLE_E2E_IDP_MAP_LOOPBACK === '1';

const PORT = Number(process.env.HONUA_CONSOLE_E2E_LIVE_PORT ?? '5176');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
const REPO_ROOT = new URL('../../', import.meta.url).pathname;

export default defineConfig({
  testDir: './live/specs',
  // The Console host is a shared singleton and these specs mutate live server state, so run them
  // serially with a single worker (mirrors the parity smoke discipline).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  // Live, server-backed specs carry inherent timing variance (cold table discovery, Blazor circuit
  // warm-up, shared-host DB load), so allow retries to absorb transient flakes.
  retries: 2,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // The governed local IdP serves a self-signed certificate. The browser must reach it
    // by the same hostname the server puts in the authorize URL (issuer parity).
    ignoreHTTPSErrors: true,
    launchOptions: MAP_IDP_TO_LOOPBACK
      ? { args: [`--host-resolver-rules=MAP ${IDP_HOSTNAME} 127.0.0.1`] }
      : {},
  },
  projects: [
    {
      name: 'setup',
      testDir: './live',
      testMatch: /operator-session\.setup\.ts/,
      use: { trace: 'on' },
    },
    {
      name: 'chromium',
      testDir: './live/specs',
      dependencies: ['setup'],
      use: {
        ...devices['Desktop Chrome'],
        storageState: 'live/.auth/operator.json',
      },
    },
  ],
  webServer: {
    command: `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      // Bind the Console to the live honua-server so Operate/Catalog surfaces hit real endpoints.
      HONUA_SERVER_BASE_URL: SERVER_URL,
      // The service key must not be available to the browser host. live/admin-api.ts reads
      // HONUA_CONSOLE_E2E_ADMIN_KEY itself for the independent verifier. An empty value
      // overrides a leaked parent HONUA_ADMIN_API_KEY, and the Console treats it as no key.
      HONUA_ADMIN_API_KEY: '',
      // The Console's non-realtime Studio builder surfaces are SHELVED (gated off by default behind
      // the studio-builders capability) in favour of the realtime SDK-driven Studio. These lanes still
      // certify those builders, so advertise the capability for the browser under test.
      HONUA_CONSOLE_CAPABILITIES: 'studio-builders',
      // Normal runs exercise the focused full Console. A zero-to-map receipt run defaults to
      // witness mode unless the caller explicitly selects another mode.
      HONUA_CONSOLE_MODE:
        process.env.HONUA_CONSOLE_MODE ?? (process.env.HONUA_ZERO_TO_MAP_RECEIPT ? 'witness' : 'full'),
    },
  },
});
