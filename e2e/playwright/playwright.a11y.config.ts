import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Accessibility gate for the Honua Console Blazor Web host (honua-server#4428).
//
// Identical boot to playwright.config.ts — the same published artifact, the same backend-free
// missing-binding mode, a separate port — but it runs ./a11y instead of ./specs. It is a separate
// config rather than extra specs in the smoke so the accessibility result is reported as its own
// lane and an a11y regression is never confused with a browser-boot regression.
//
// Original smoke header follows.
// Thin real-browser smoke for the Honua Console Blazor Web host.
//
// The host runs WITHOUT a backend (missing-binding / demo mode), so the smoke needs no
// live honua-server. We boot the published artifact when HONUA_CONSOLE_E2E_DLL points at a
// built `Honua.Console.Web.dll` (CI path), and otherwise fall back to `dotnet run` against
// the source project (local path). Both bind 127.0.0.1:5174 over plain HTTP.

// A distinct default port so a local a11y run can sit alongside a running smoke host.
const PORT = Number(process.env.HONUA_CONSOLE_A11Y_PORT ?? process.env.HONUA_CONSOLE_E2E_PORT ?? '5178');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

// Published-artifact path (preferred in CI): `dotnet <dll> --urls ...`.
// Source path (local default): `dotnet run --project ... --urls ...`.
const publishedDll = process.env.HONUA_CONSOLE_E2E_DLL;
// When booting the published artifact, the content root MUST be the publish directory so
// MapStaticAssets resolves `_content/**` from `<publishDir>/wwwroot`. Running the DLL from the
// repo root resolves the wrong web root and serves empty (Content-Length: 0) static assets.
const publishDir = publishedDll ? path.dirname(publishedDll) : undefined;
const webServerCommand = publishedDll
  ? `dotnet "${publishedDll}" --urls ${BASE_URL} --contentRoot "${publishDir}"`
  : `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`;

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
      // The Console's non-realtime Studio builder surfaces are SHELVED (gated off by default behind
      // the studio-builders capability) in favour of the realtime SDK-driven Studio. These lanes still
      // certify those builders, so advertise the capability for the browser under test.
      HONUA_CONSOLE_CAPABILITIES: 'studio-builders',
    },
  },
});
