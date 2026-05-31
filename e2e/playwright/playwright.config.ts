import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

// Thin real-browser smoke for the Honua Console Blazor Web host.
//
// The host runs WITHOUT a backend (missing-binding / demo mode), so the smoke needs no
// live honua-server. We boot the published artifact when HONUA_CONSOLE_E2E_DLL points at a
// built `Honua.Console.Web.dll` (CI path), and otherwise fall back to `dotnet run` against
// the source project (local path). Both bind 127.0.0.1:5174 over plain HTTP.

const PORT = Number(process.env.HONUA_CONSOLE_E2E_PORT ?? '5174');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const REPO_ROOT = new URL('../../', import.meta.url).pathname;

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
  testDir: './specs',
  // Fully serial + single worker: the host is a shared singleton and the smoke is
  // deliberately deterministic (no parallel circuits competing on one server).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  // Browser smokes can be marginally flaky on cold CI runners; allow a couple of retries
  // but keep the suite advisory (see .github/workflows/console-e2e.yml) so a transient
  // failure never blocks merge.
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
      // Keep the host strictly in missing-binding mode: no server base URL bound.
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    },
  },
});
