// Test-only server-host regression; shipping WASM is qualified by playwright.static.config.ts.
import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Legacy server-host accessibility regression (honua-server#4428).
//
// Identical boot to playwright.config.ts — the same legacy fixture, the same backend-free
// missing-binding mode, a separate port — but it runs ./a11y instead of ./specs. It is a separate
// config rather than extra specs in the smoke so the accessibility result is reported as its own
// lane and an a11y regression is never confused with a browser-boot regression.
//
// Original smoke header follows.
// Browser regression for the non-shipping legacy server host.
//
// The host runs WITHOUT a backend (missing-binding / demo mode), so the smoke needs no
// live honua-server. We boot the legacy fixture when HONUA_CONSOLE_LEGACY_DLL points at a
// built `Honua.Console.LegacyHost.dll` (CI path), and otherwise fall back to `dotnet run` against
// the source project (local path). Both bind the configured loopback port over plain HTTP.

// A distinct default port so a local a11y run can sit alongside a running smoke host.
const PORT = Number(process.env.HONUA_CONSOLE_A11Y_PORT ?? process.env.HONUA_CONSOLE_E2E_PORT ?? '5178');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

// Built-fixture path (preferred in CI): `dotnet <dll> --urls ...`.
// Source path (local default): `dotnet run --project ... --urls ...`.
const legacyDll = process.env.HONUA_CONSOLE_LEGACY_DLL;
// Resolve the built fixture content root; its static-web-assets manifest links source assets.
const fixtureDir = legacyDll ? path.dirname(legacyDll) : undefined;
const webServerCommand = legacyDll
  ? `dotnet "${legacyDll}" --urls ${BASE_URL} --contentRoot "${fixtureDir}"`
  : `dotnet run --project tests/Honua.Console.LegacyHost/Honua.Console.LegacyHost.csproj --urls ${BASE_URL}`;

export default defineConfig({
  testDir: './a11y',
  // Fully serial + single worker: the host is a shared singleton and the smoke is
  // deliberately deterministic (no parallel circuits competing on one server).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  // Retry transient browser startup failures on cold CI runners. Exhausting
  // retries fails the accessibility step in console-e2e.yml.
  retries: process.env.CI ? 2 : 0,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['list']]
    : [['list']],
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
    command: webServerCommand,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      // Keep the host strictly in missing-binding mode regardless of the ambient environment.
      // Playwright's webServer inherits process.env, so if HONUA_SERVER_BASE_URL (or the
      // Honua__Server__* config keys) are set on the runner / dev machine, Program.cs would
      // bind live honua-server services and the no-backend assertions would break. Explicitly
      // clear every server-binding key Program.cs reads so the smoke is always backend-free.
      HONUA_SERVER_BASE_URL: '',
      HONUA_ADMIN_API_KEY: '',
      HONUA_SERVER_PUBLICATION_IDS: '',
      Honua__Server__BaseUrl: '',
      Honua__Server__AdminApiKey: '',
      Honua__Server__PublicationIds: '',
    },
  },
});
