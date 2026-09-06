import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';

const root = new URL('../../', import.meta.url).pathname;
const dll = process.env.HONUA_CONSOLE_E2E_DLL;
const contentRoot = dll ? path.dirname(dll) : undefined;
const server = (mode: string, port: number) => ({
  command: dll
    ? `dotnet "${dll}" --urls http://127.0.0.1:${port} --contentRoot "${contentRoot}"`
    : `dotnet run --no-build --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls http://127.0.0.1:${port}`,
  cwd: root, url: `http://127.0.0.1:${port}/version.json`, timeout: 120_000,
  env: {
    ASPNETCORE_ENVIRONMENT: 'Production',
    Honua__Console__Mode: mode,
    Honua__Console__Auth__Mode: 'EdgeForwarded',
    Honua__Console__Auth__EdgeForwarded__SharedSecret: 'operator-test-edge-secret',
    Honua__Server__BaseUrl: 'http://127.0.0.1:5198',
    Honua__Server__AdminApiKey: 'must-never-reach-upstream',
    HONUA_SERVER_BASE_URL: 'http://127.0.0.1:5198',
    HONUA_ADMIN_API_KEY: 'must-never-reach-upstream',
  },
});

export default defineConfig({
  testDir: './operator', workers: 1, retries: 0, timeout: 30_000,
  use: { ...devices['Desktop Chrome'] },
  projects: [
    { name: 'full', use: { baseURL: 'http://127.0.0.1:5197' } },
    { name: 'witness', use: { baseURL: 'http://127.0.0.1:5199' } },
  ],
  webServer: [server('full', 5197), server('witness', 5199)],
});
