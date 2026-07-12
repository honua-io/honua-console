import { defineConfig, devices } from '@playwright/test';
import { fileURLToPath } from 'node:url';

// Live-LLM smoke lane (honua-console#283).
//
// Drives the STOCK Console Web host against a REAL honua-server that is wired to
// a REAL AI generation provider, so the studio generate-from-prompt surfaces
// (QUERY / MAP / FORM / WORKFLOW) exercise genuine prompt -> LLM -> result
// generation — the one thing the deterministic live lane cannot cover. LLM
// output is non-deterministic, so every assertion here is TOLERANT ("a coherent
// generated result appeared"), never exact-structure.
//
// This lane is NON-BLOCKING: it runs nightly (and on demand), gated on an LLM
// credential. Without a configured provider the specs SKIP (they never fail the
// build), and if the server reports the surface `unsupported` they annotate and
// skip cleanly rather than failing.
//
// It does NOT boot the server stack; the orchestrator does that first:
//   node e2e/run-live-llm.mjs
// which brings up e2e/playwright/live-llm/docker-compose.yml, resolves an LLM
// provider (Bedrock via LiteLLM, or a direct OpenAI-compatible key), pre-starts
// the Console, and runs this config.
//
// Run directly (stack already up + provider env exported):
//   npx playwright test --config playwright.live-llm.config.ts

const PORT = Number(process.env.HONUA_CONSOLE_E2E_LIVE_LLM_PORT ?? '5674');
const BASE_URL = `http://127.0.0.1:${PORT}`;
const SERVER_URL = process.env.HONUA_CONSOLE_E2E_LIVE_LLM_SERVER_URL ?? 'http://127.0.0.1:5681';
const ADMIN_KEY = process.env.HONUA_CONSOLE_E2E_ADMIN_KEY ?? 'honua-console-dev-key';
// fileURLToPath keeps the cwd valid on Windows (URL.pathname yields /C:/...).
const REPO_ROOT = fileURLToPath(new URL('../../', import.meta.url));

export default defineConfig({
  testDir: './live-llm/specs',
  // One Console process; generation turns are slow, so run serially, no retries
  // (a retried non-deterministic LLM call proves nothing).
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: 0,
  // Generation round-trips through a real model; give it room.
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
  webServer: {
    command: `dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj --urls ${BASE_URL}`,
    cwd: REPO_ROOT,
    url: `${BASE_URL}/version.json`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Development',
      DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      HONUA_SERVER_BASE_URL: SERVER_URL,
      // The Console authenticates to the server with the admin key; the same key
      // the specs use for the independent server-side generation assertion.
      HONUA_ADMIN_API_KEY: ADMIN_KEY,
    },
  },
});
