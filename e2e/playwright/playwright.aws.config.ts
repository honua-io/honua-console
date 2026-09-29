import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PORT = Number(process.env.HONUA_CONSOLE_AWS_PORT ?? '5177');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_AWS_SERVER_URL ?? 'https://demo.honua.io';
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));
const publishedDll = process.env.HONUA_CONSOLE_E2E_DLL;
const publishDir = publishedDll ? path.dirname(publishedDll) : undefined;
const webServerCommand = publishedDll
  ? `dotnet "${publishedDll}" --urls ${BASE_URL} --contentRoot "${publishDir}"`
  : `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`;

// #360 removed the shared admin-key fallback from the browser transport: every privileged
// read now runs as a governed operator, so the smoke needs its own dedicated operator's
// credentials rather than a server admin key. The credentials are read directly from the
// process environment by aws/specs/live-demo.aws.spec.ts (never passed to the Console
// process itself, which never sees a shared key).
if (!process.env.HONUA_CONSOLE_AWS_OPERATOR_USERNAME || !process.env.HONUA_CONSOLE_AWS_OPERATOR_PASSWORD) {
  throw new Error(
    'HONUA_CONSOLE_AWS_OPERATOR_USERNAME and HONUA_CONSOLE_AWS_OPERATOR_PASSWORD are required for the live AWS browser smoke',
  );
}

export default defineConfig({
  testDir: './aws/specs',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  reporter: process.env.CI
    ? [['html', { open: 'never' }], ['json', { outputFile: '.tmp/aws-console-report.json' }], ['list']]
    : [['list']],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: webServerCommand,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      HONUA_SERVER_BASE_URL: SERVER_URL,
    },
  },
});
