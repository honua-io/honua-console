// Test-only server-host regression; shipping WASM is qualified by playwright.static.config.ts.
import { defineConfig, devices } from '@playwright/test';
import path from 'node:path';
import { mkdirSync, mkdtempSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

const root = new URL('../../', import.meta.url).pathname;
const artifacts = path.join(root, 'artifacts');
mkdirSync(artifacts, { recursive: true });
const tls = mkdtempSync(path.join(artifacts, 'operator-tls-'));
const certificate = path.join(tls, 'certificate.pem');
const key = path.join(tls, 'key.pem');
execFileSync('openssl', ['req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
  '-subj', '/CN=localhost', '-addext', 'subjectAltName=IP:127.0.0.1', '-out', certificate, '-keyout', key], { stdio: 'ignore' });
const dll = process.env.HONUA_CONSOLE_LEGACY_DLL;
const contentRoot = dll ? path.dirname(dll) : undefined;
const server = (mode: string, port: number) => ({
  command: dll
    ? `dotnet "${dll}" --urls https://127.0.0.1:${port} --contentRoot "${contentRoot}"`
    : `dotnet run --no-build --no-launch-profile --project tests/Honua.Console.LegacyHost/Honua.Console.LegacyHost.csproj --urls https://127.0.0.1:${port}`,
  ignoreHTTPSErrors: true, cwd: root, url: `https://127.0.0.1:${port}/version.json`, timeout: 120_000,
  env: {
    ASPNETCORE_ENVIRONMENT: 'Production',
    Kestrel__Certificates__Default__Path: certificate,
    Kestrel__Certificates__Default__KeyPath: key,
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
  use: { ...devices['Desktop Chrome'], ignoreHTTPSErrors: true },
  projects: [
    { name: 'full', use: { baseURL: 'https://127.0.0.1:5197' } },
    { name: 'witness', use: { baseURL: 'https://127.0.0.1:5199' } },
  ],
  webServer: [server('full', 5197), server('witness', 5199)],
});
