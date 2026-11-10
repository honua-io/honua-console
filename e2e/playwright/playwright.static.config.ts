import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

const origin = `http://127.0.0.1:${process.env.HONUA_CONSOLE_STATIC_PORT ?? '5184'}`;

export default defineConfig({
  testDir: './static',
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 30_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: { baseURL: origin, trace: 'retain-on-failure', screenshot: 'only-on-failure' },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 900 } } },
    { name: 'mobile', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } },
  ],
  webServer: {
    command: 'node e2e/playwright/static-bundle-server.mjs',
    cwd: fileURLToPath(new URL('../../', import.meta.url)),
    url: `${origin}/console/index.html`,
    reuseExistingServer: false,
  },
});
