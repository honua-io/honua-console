// Build-time constants stamped by vite.config.ts (`define`). Re-exported as a
// typed object so the rest of the app does not depend on global magic names
// and release tooling can pull a single source of truth.

declare const __HONUA_CONSOLE_VERSION__: string;
declare const __HONUA_CONSOLE_COMMIT__: string;
declare const __HONUA_CONSOLE_SHORT_COMMIT__: string;
declare const __HONUA_CONSOLE_REF__: string;
declare const __HONUA_CONSOLE_BUILT_AT__: string;
declare const __HONUA_CONSOLE_MODE__: string;

export interface BuildInfo {
  name: "honua-console";
  version: string;
  commit: string;
  shortCommit: string;
  ref: string;
  builtAt: string;
  mode: string;
}

export const BUILD_INFO: BuildInfo = {
  name: "honua-console",
  version: __HONUA_CONSOLE_VERSION__,
  commit: __HONUA_CONSOLE_COMMIT__,
  shortCommit: __HONUA_CONSOLE_SHORT_COMMIT__,
  ref: __HONUA_CONSOLE_REF__,
  builtAt: __HONUA_CONSOLE_BUILT_AT__,
  mode: __HONUA_CONSOLE_MODE__,
};
