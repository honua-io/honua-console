import { defineConfig, devices } from '@playwright/test';

// Live-backed real-browser e2e for the Honua Console.
//
// Unlike playwright.config.ts (which runs the host strictly backend-free for missing-binding
// smoke), this config boots the Console BOUND to a real honua-server and drives create/test
// flows that mutate live server state, verifying the result through the server admin API.
//
// Prerequisites it does NOT boot (provide them first):
//   - honua-server reachable at HONUA_CONSOLE_E2E_SERVER_URL with HONUA_CONSOLE_E2E_ADMIN_KEY,
//     and its backing databases. Locally that is the `console-testbed` docker compose stack plus
//     the provider-aware source server; in CI it is the pinned server image + DB services.
// Playwright boots ONLY the Console (bound to that server) on HONUA_CONSOLE_E2E_LIVE_PORT.

const PORT = Number(process.env.HONUA_CONSOLE_E2E_LIVE_PORT ?? '5176');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_E2E_SERVER_URL ?? 'http://127.0.0.1:8088';
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
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
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      // Bind the Console to the live honua-server so Operate/Catalog surfaces hit real endpoints.
      HONUA_SERVER_BASE_URL: SERVER_URL,
      HONUA_ADMIN_API_KEY: ADMIN_KEY,
      // The Console's non-realtime Studio builder surfaces are SHELVED (gated off by default behind
      // the studio-builders capability) in favour of the realtime SDK-driven Studio. These lanes still
      // certify those builders, so advertise the capability for the browser under test.
      HONUA_CONSOLE_CAPABILITIES: 'studio-builders',
    },
  },
});
