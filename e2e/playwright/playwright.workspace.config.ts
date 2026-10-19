import { defineConfig, devices } from '@playwright/test';

function required(name: string): string {
  const value = process.env[name]?.trim();
  if (!value) throw new Error(`${name} is required for the installed workspace journey.`);
  return value;
}

const origin = new URL(required('HONUA_WORKSPACE_E2E_ORIGIN'));
if (!['http:', 'https:'].includes(origin.protocol) || origin.username || origin.password ||
    origin.pathname !== '/' || origin.search || origin.hash) {
  throw new Error('HONUA_WORKSPACE_E2E_ORIGIN must be an HTTP(S) origin without credentials.');
}

// This lane consumes an already installed Console and an actual paired/signed-in
// human session. It never starts a source checkout or substitutes an admin key.
export default defineConfig({
  testDir: '.',
  testMatch: ['**/workspace-live/specs/*.spec.ts', '**/workspace/*.spec.ts'],
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 480_000,
  expect: { timeout: 30_000 },
  reporter: [['list']],
  use: {
    baseURL: origin.origin,
    storageState: required('HONUA_WORKSPACE_E2E_OPERATOR_STATE'),
    // Authenticated transport includes cookies/CSRF material. Attach a bounded,
    // credential-free receipt instead of uploading network traces or videos.
    trace: 'off',
    video: 'off',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
