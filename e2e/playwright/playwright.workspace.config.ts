import { defineConfig, devices } from '@playwright/test';

// This lane consumes the deployed candidate. It never boots a legacy Console or substitutes APIs.
const configured = process.env.HONUA_WORKSPACE_E2E_ORIGIN;
if (!configured) throw new Error('HONUA_WORKSPACE_E2E_ORIGIN must name the deployed unified-workspace candidate.');
const origin = new URL(configured);
if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password
    || origin.pathname !== '/' || origin.search || origin.hash)
  throw new Error('HONUA_WORKSPACE_E2E_ORIGIN must be a credential-free HTTP(S) origin.');
const auth = process.env.HONUA_WORKSPACE_E2E_AUTH;
if (!['local', 'oidc', 'storage'].includes(auth ?? ''))
  throw new Error('Set HONUA_WORKSPACE_E2E_AUTH=local|oidc|storage explicitly.');
if (auth === 'storage' && !process.env.HONUA_WORKSPACE_E2E_STORAGE_STATE)
  throw new Error('Storage auth requires HONUA_WORKSPACE_E2E_STORAGE_STATE.');

export default defineConfig({
  testDir: './workspace', testMatch: '**/*.spec.ts', fullyParallel: false, workers: 1,
  forbidOnly: !!process.env.CI, retries: 0, timeout: 240_000, expect: { timeout: 30_000 },
  reporter: [['list']],
  use: {
    baseURL: origin.origin,
    ...(auth === 'storage' ? {storageState: process.env.HONUA_WORKSPACE_E2E_STORAGE_STATE} : {}),
    // Login, CSRF and signed model traffic must not be exported in network traces.
    trace: 'off', screenshot: 'only-on-failure',
  },
  projects: [{name: 'chromium', use: {...devices['Desktop Chrome']}}],
});
