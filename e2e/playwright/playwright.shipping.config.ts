import { defineConfig } from '@playwright/test';

// The packaged quickstart serves the released WASM bundle and real API together.
// No dotnet webServer or browser route interception participates in this suite.
export default defineConfig({
  testDir: './shipping',
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env.CI,
  timeout: 90_000,
  expect: { timeout: 30_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.HONUA_SHIPPING_BASE_URL ?? 'https://localhost:18083',
    ignoreHTTPSErrors: true,
    locale: 'en-US',
    // Traces can retain the password POST and session headers. Keep screenshots only.
    trace: 'off',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1280, height: 900 } } },
    { name: 'mobile', use: { viewport: { width: 390, height: 844 } } },
  ],
});
