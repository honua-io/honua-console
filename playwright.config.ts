import { defineConfig, devices } from "@playwright/test";

const PORT = Number(process.env.CONSOLE_SMOKE_PORT ?? 4173);
const BASE_URL = process.env.CONSOLE_SMOKE_BASE_URL ?? `http://127.0.0.1:${PORT}`;

// Vite bakes VITE_* values at build time. Seed deterministic values here so
// smoke runs do not depend on a developer's local .env. Real deployments
// override via their own preview/release pipelines (honua-devops#56).
const SMOKE_ADMIN_URL = process.env.VITE_ADMIN_BASE_URL ?? "https://admin.smoke.honua.example";
const SMOKE_API_URL = process.env.VITE_API_BASE_URL ?? "https://api.smoke.honua.example";

export default defineConfig({
  testDir: "./tests/smoke",
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL: BASE_URL,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: `npm run build --silent && npm run preview --silent -- --port ${PORT}`,
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      VITE_ADMIN_BASE_URL: SMOKE_ADMIN_URL,
      VITE_API_BASE_URL: SMOKE_API_URL,
      VITE_AUTH_DRIVER: "fixture",
    },
  },
});
