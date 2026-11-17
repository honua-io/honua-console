import { defineConfig, devices } from '@playwright/test';
import { gateConsoleUrl } from './gate/stack-url.mjs';

// Consume an installed stack; never boot a checkout or substitute a backend.
const baseURL = gateConsoleUrl(process.env.HONUA_GATE_STACK_URL);
const storageState = process.env.HONUA_WORKSPACE_E2E_OPERATOR_STATE;
if (!storageState) throw new Error('HONUA_WORKSPACE_E2E_OPERATOR_STATE must name an existing human session.');

export default defineConfig({
  testDir: './gate',
  testMatch: '*.spec.ts',
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 30_000 },
  forbidOnly: !!process.env.CI,
  reporter: [['list']],
  use: {
    baseURL, storageState,
    // Human sessions contain credentials. Never record transport or video.
    trace: 'off', video: 'off', screenshot: 'off',
  },
  projects: [1280, 390].map(width => ({
    name: `gate-${width}`,
    use: { ...devices['Desktop Chrome'], viewport: { width, height: width === 390 ? 844 : 900 } },
  })),
});
