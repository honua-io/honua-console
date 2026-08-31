import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

const port = Number(process.env.HONUA_CONSOLE_FOCUSED_PORT ?? '5275');
const baseURL = process.env.HONUA_CONSOLE_FOCUSED_ORIGIN ?? `http://127.0.0.1:${port}`;
const serverURL = process.env.HONUA_CONSOLE_FOCUSED_SERVER_URL ?? 'http://127.0.0.1:5281';
const repoRoot = fileURLToPath(new URL('../../', import.meta.url));
const externalConsole = Boolean(process.env.HONUA_CONSOLE_FOCUSED_ORIGIN);

export default defineConfig({
  testDir: './focused/specs', fullyParallel: false, workers: 1, forbidOnly: !!process.env.CI,
  retries: 0, timeout: 180_000, expect: { timeout: 20_000 }, reporter: [['list']],
  use: {
    baseURL, trace: 'retain-on-failure', screenshot: 'only-on-failure', ignoreHTTPSErrors: true,
    launchOptions: { args: ['--host-resolver-rules=MAP host.docker.internal 127.0.0.1'] },
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: externalConsole ? undefined : {
    command: `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${baseURL}`,
    cwd: repoRoot, url: `${baseURL}/version.json`, reuseExistingServer: !process.env.CI, timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development', DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      HONUA_SERVER_BASE_URL: serverURL,
      // Intentionally no HONUA_ADMIN_API_KEY: interactive reads must fail closed unless the
      // active operator completes the bearer exchange.
    },
  },
});
