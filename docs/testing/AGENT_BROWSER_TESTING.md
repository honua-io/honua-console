# Console browser testing

The shipping Console is a static Blazor WebAssembly SPA. The browser loads the
bundle at `/console/` and calls Honua Server APIs directly using the configured
API base URL and the user's session or bearer token. A server-hosted Blazor
fixture remains in this repository for regression tests; its results do not
qualify the shipping SPA.

## Choose the evidence you need

| Goal | Harness | What a pass proves |
| --- | --- | --- |
| Published WASM boot, deep links and browser transport contracts | [`playwright.static.config.ts`](../../e2e/playwright/playwright.static.config.ts) | Published files work in desktop and mobile Chromium; transport fixtures exercise API contracts |
| Legacy host rendering and missing-binding behavior | `npm run e2e:smoke` | Test-only server-hosted components render |
| Legacy host workflows against a real server | `npm run e2e:live` | The selected live backend works with the regression fixture; see the [live harness guide](../../e2e/README.md) |
| Release candidate customer journey | Deployed static bundle and live API, with an authenticated test operator | The recorded journey works on that particular Console/server pair |

The static suite uses local contract fixtures. A green result does not establish
live sign-in, approval, provider execution or deployment readiness. Node tests
and HTTP smoke checks are also useful but do not substitute for a browser journey.

## Test a published static bundle

Use the [`Console static bundle` workflow](../../.github/workflows/static-bundle.yml)
as the canonical build recipe. It selects the SDK pinned in
`src/Honua.Console.Web/global.json`, restores locked dependencies, fetches and
verifies Cesium and Studio assets, publishes WASM, runs browser tests and packages
the release files. Do not substitute a legacy-host build for this artifact.
Honua lanes run all .NET builds and tests through `remote-dotnet` or `remote-run`
on the build host, including harnesses that start .NET implicitly.

With an already published bundle available, run these commands from the
repository root on the browser test machine:

```sh
npm ci --prefix e2e/playwright
(cd e2e/playwright && npx --no-install playwright install --with-deps chromium)
(cd e2e/playwright && npx --no-install playwright test --config playwright.static.config.ts)
```

By default the suite serves `artifacts/static-publish/wwwroot/console` at
`http://127.0.0.1:5184/console/`. Contract fixtures also use fixed ports
5185, 5186, 5187 and 5188.
Set `HONUA_CONSOLE_STATIC_ROOT` to an absolute path to the extracted bundle's
Console files if they are elsewhere. `HONUA_CONSOLE_STATIC_PORT` overrides the
first port only; the contract fixtures still need 5185–5188. Keep the default
range 5184–5188 free, or the overridden first port plus 5185–5188.

Playwright retains failure traces and screenshots and writes an HTML report.
Inspect these together with browser console and network errors before changing
the application. The fixtures are test infrastructure, not deployment servers.

## Check a live candidate in a browser

Use the immutable Console bundle and server image selected for qualification.
Record their versions/digests, public Console URL, API origin, authentication
mode, journey steps and observed result. Preserve redacted browser/network
failure evidence; never include credentials, cookies, bearer tokens or CSRF
tokens in a report.

Check the deployed `config.json` at the Console mount: `apiBaseUrl` and `auth`
must describe the intended server and identity provider. Exercise sign-in and
refresh, navigate to a deep link, perform the task with an authenticated operator,
and verify its persisted result through the live API. For approval journeys,
verify the approval result and the destination after navigation as well.

For same-origin hosting, the browser uses the server's session cookie and
identity-bound CSRF token for mutations. For a separate Console origin, verify
the configured bearer flow and the server's explicit CORS allowlist. Never put
an admin API key in the static bundle or browser configuration.

An interactive browser tool, including Playwright MCP when installed, can drive
these checks. The repository's Playwright tests also drive real browsers; MCP is
not required to run them. A fixture response or scripted provider answer must be
identified as such and cannot be recorded as a live release pass.

## Troubleshooting

- **Bundle fails before rendering:** inspect failed `_framework` requests, WASM
  content types, missing assets and the `/console/` base path. Follow the static
  workflow's asset verification steps.
- **Deep link returns 404:** check that the host serves the SPA entry point for
  Console navigation while leaving API routes with the API origin.
- **401 or 403 on a live mutation:** inspect session expiry, operator permissions,
  token audience, CSRF and the configured API origin. A working admin-key verifier
  does not establish that the browser operator is authenticated.
- **Separate-origin requests fail:** inspect CORS and the bearer flow rather than
  adding credentials to the bundle.
- **Legacy smoke passes but the candidate fails:** reproduce against the deployed
  WASM artifact; the legacy fixture has a different host and authentication path.
