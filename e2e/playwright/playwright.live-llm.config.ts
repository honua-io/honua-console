import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

// Live-LLM smoke lane.
//
// Drives the legacy regression fixture's /support GIS assistant against a REAL
// AI provider: the Console surface that still runs live inference since ADR-0076
// retired server-side generation. Model output is non-deterministic, so the
// answer check is TOLERANT ("a substantive reply appeared").
//
// The lane is NON-BLOCKING: it runs nightly (and on demand), gated on an LLM
// credential. Without a configured provider the spec SKIPS.
//
// The orchestrator normally owns the Console and the provider:
//   node e2e/run-live-llm.mjs
// It resolves a provider (Bedrock via a LiteLLM sidecar, or a direct
// OpenAI-compatible key), starts the Console with HONUA_LLM_* set, and runs this
// config. Running this config directly starts the Console itself from the
// HONUA_LLM_* variables in your environment:
//   HONUA_LIVE_LLM_ENABLED=1 HONUA_LLM_BASE_URL=... HONUA_LLM_MODEL=... \
//     npx playwright test --config playwright.live-llm.config.ts

const PORT = Number(process.env.HONUA_CONSOLE_E2E_LIVE_LLM_PORT ?? '5674');
const BASE_URL = `http://127.0.0.1:${PORT}`;
// fileURLToPath keeps the cwd valid on Windows (URL.pathname yields /C:/...).
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

export default defineConfig({
  testDir: './live-llm/specs',
  // One Console process; model turns are slow, so run serially, no retries
  // (a retried non-deterministic LLM call proves nothing).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  // The answer round-trips through a real model; give it room.
  timeout: 240_000,
  expect: { timeout: 30_000 },
  reporter: [['list'], ['json', { outputFile: 'live-llm/results/results.json' }]],
  use: {
    baseURL: BASE_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // Gated mode (no provider) skips the spec, so it boots no Console.
  webServer: process.env.HONUA_LIVE_LLM_ENABLED !== '1' ? undefined : {
    command: `dotnet run --project tests/Honua.Console.LegacyHost/Honua.Console.LegacyHost.csproj --urls ${BASE_URL}`,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    // The orchestrator starts the Console before Playwright; reuse it.
    reuseExistingServer: true,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      HONUA_LLM_BASE_URL: process.env.HONUA_LLM_BASE_URL ?? '',
      HONUA_LLM_MODEL: process.env.HONUA_LLM_MODEL ?? '',
      HONUA_LLM_API_KEY: process.env.HONUA_LLM_API_KEY ?? '',
    },
  },
});
