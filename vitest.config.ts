import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Mirror the build-time defines from vite.config.ts so unit tests can render
// components that read from src/build-info.ts without crashing. Keep these
// values deterministic so snapshot/dom assertions stay stable.
const TEST_BUILD_DEFINES = {
  __HONUA_CONSOLE_VERSION__: JSON.stringify("test"),
  __HONUA_CONSOLE_COMMIT__: JSON.stringify("0000000000000000000000000000000000000000"),
  __HONUA_CONSOLE_SHORT_COMMIT__: JSON.stringify("000000000000"),
  __HONUA_CONSOLE_REF__: JSON.stringify("test"),
  __HONUA_CONSOLE_BUILT_AT__: JSON.stringify("1970-01-01T00:00:00.000Z"),
  __HONUA_CONSOLE_MODE__: JSON.stringify("test"),
};

export default defineConfig({
  plugins: [react()],
  define: TEST_BUILD_DEFINES,
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./tests/setup.ts"],
    include: ["tests/**/*.test.{ts,tsx}", "src/**/*.test.{ts,tsx}"],
  },
});
