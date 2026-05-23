import { defineConfig, devices } from "@playwright/test";

const PORT = Number(process.env.CONSOLE_SMOKE_PORT ?? 4173);
const BASE_URL = process.env.CONSOLE_SMOKE_BASE_URL ?? `http://127.0.0.1:${PORT}`;

export default defineConfig({
  testDir: "./tests/smoke",
  testIgnore: "app-builder-proof.spec.ts",
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
      VITE_SESSION_DRIVER: "fixture",
    },
  },
});
