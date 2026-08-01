import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PORT = Number(process.env.HONUA_CONSOLE_AWS_PORT ?? '5177');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_AWS_SERVER_URL ?? 'https://demo.honua.io';
const ADMIN_KEY = process.env.HONUA_CONSOLE_AWS_ADMIN_KEY;
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));
const publishedDll = process.env.HONUA_CONSOLE_E2E_DLL;
const publishDir = publishedDll ? path.dirname(publishedDll) : undefined;
const webServerCommand = publishedDll
  ? `dotnet "${publishedDll}" --urls ${BASE_URL} --contentRoot "${publishDir}"`
  : `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`;

if (!ADMIN_KEY) {
  throw new Error('HONUA_CONSOLE_AWS_ADMIN_KEY is required for the live AWS browser smoke');
}

export default defineConfig({
  testDir: './aws/specs',
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
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
      HONUA_ADMIN_API_KEY: ADMIN_KEY,
    },
  },
});
