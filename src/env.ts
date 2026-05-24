/**
 * Single source of truth for Honua Console environment configuration.
 *
 * All `VITE_*` reads must happen here. Modules and route components consume the
 * `consoleEnv` object so that any later move to a typed env loader (e.g. honua-devops#56
 * preview/release pipeline) only touches this file.
 */

export type AuthDriverName = "fixture" | "whoami";

export interface ConsoleEnv {
  /** Base URL for Honua Server REST calls; absolute URL or empty for same-origin proxy. */
  readonly apiBaseUrl: string;
  /**
   * Legacy admin (Blazor) base URL. Used by `useAdminLink` while the Operate
   * port is in flight (honua-console#6). Empty disables the link-back.
   */
  readonly adminBaseUrl: string;
  /** Selected session driver. */
  readonly authDriver: AuthDriverName;
  /** Optional override for the whoami endpoint when `authDriver === "whoami"`. */
  readonly whoamiUrl: string;
  /** Server-owned sign-in endpoint used by the whoami driver. */
  readonly authSignInUrl: string;
  /** Server-owned sign-out endpoint used by the whoami driver. */
  readonly authSignOutUrl: string;
  /** Optional JSON authenticated fixture session seed for local tests. */
  readonly fakeSessionSeed: string;
  /** Comma-separated feature flags. Empty unless explicitly configured. */
  readonly featureFlags: ReadonlySet<string>;
}

function readEnv(key: string): string {
  const raw = (import.meta.env as Record<string, string | undefined>)[key];
  return typeof raw === "string" ? raw.trim() : "";
}

function readBooleanEnv(key: string): boolean {
  const value = readEnv(key).toLowerCase();
  if (!value) return false;
  if (value === "true" || value === "1" || value === "yes") return true;
  if (value === "false" || value === "0" || value === "no") return false;
  throw new Error(`${key} must be 'true' or 'false' (got '${readEnv(key)}').`);
}

function isProductionBuild(): boolean {
  return import.meta.env.PROD === true || import.meta.env.MODE === "production";
}

interface ParseAuthDriverOptions {
  readonly allowFixtureAuth: boolean;
  readonly productionBuild: boolean;
}

export function parseAuthDriver(raw: string, options: ParseAuthDriverOptions): AuthDriverName {
  const value = raw.toLowerCase();
  if (value === "whoami") return "whoami";
  if (!value) return options.productionBuild ? "whoami" : "fixture";
  if (value === "fixture") {
    if (options.productionBuild && !options.allowFixtureAuth) {
      throw new Error("VITE_AUTH_DRIVER=fixture requires VITE_ALLOW_FIXTURE_AUTH=true in production builds.");
    }
    return "fixture";
  }
  throw new Error(`VITE_AUTH_DRIVER must be 'fixture' or 'whoami' (got '${raw}').`);
}

function parseFlags(raw: string): ReadonlySet<string> {
  if (!raw) return new Set();
  return new Set(
    raw
      .split(",")
      .map((entry) => entry.trim())
      .filter(Boolean),
  );
}

export function loadConsoleEnv(): ConsoleEnv {
  const allowFixtureAuth = readBooleanEnv("VITE_ALLOW_FIXTURE_AUTH");
  return {
    apiBaseUrl: readEnv("VITE_API_BASE_URL"),
    adminBaseUrl: readEnv("VITE_ADMIN_BASE_URL"),
    authDriver: parseAuthDriver(readEnv("VITE_AUTH_DRIVER"), {
      allowFixtureAuth,
      productionBuild: isProductionBuild(),
    }),
    whoamiUrl: readEnv("VITE_WHOAMI_URL") || "/api/portal/whoami",
    authSignInUrl: readEnv("VITE_AUTH_SIGN_IN_URL") || "/api/auth/signin",
    authSignOutUrl: readEnv("VITE_AUTH_SIGN_OUT_URL") || "/api/auth/signout",
    fakeSessionSeed: readEnv("VITE_FAKE_SESSION"),
    featureFlags: parseFlags(readEnv("VITE_FEATURE_FLAGS")),
  };
}

/**
 * Module-load env snapshot. Reads `import.meta.env` once at module load so
 * misconfiguration fails fast on first paint instead of on the first API call.
 */
export const consoleEnv: ConsoleEnv = loadConsoleEnv();
