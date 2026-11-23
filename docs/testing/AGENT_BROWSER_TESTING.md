# Console browser testing

The shipping Console is a static Blazor WebAssembly SPA. The browser loads the
bundle at `/console/` and calls Honua Server APIs directly using the configured
API base URL and the user's session or bearer token. A server-hosted Blazor
fixture remains in this repository for regression tests; its results do not
qualify the shipping SPA.

For release qualification of the shipping WebAssembly Console, use
[Shipping Console live replay](SHIPPING_LIVE_REPLAY.md). That suite runs the
released bundle against the packaged HTTPS quickstart with a real server at
1280 and 390 widths.

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

Extract the published bundle's Console files into
`artifacts/static-publish/wwwroot/console` (so that `index.html` is directly in
that directory). With that bundle available, run these commands from the
repository root on the browser test machine:

```sh
npm ci --prefix e2e/playwright
(cd e2e/playwright && npx --no-install playwright install --with-deps chromium)
(cd e2e/playwright && npx --no-install playwright test --config playwright.static.config.ts)
```

By default the suite serves that directory at
`http://127.0.0.1:5184/console/`. Contract fixtures also use fixed ports
5185, 5186, 5187, 5188, 5195 and 5196.
`HONUA_CONSOLE_STATIC_ROOT` overrides the bundle path for the boot and transport
fixtures only; the alert fixtures still require the default directory above.
`HONUA_CONSOLE_STATIC_PORT` overrides the first port only. Keep ports 5184–5188,
5195 and 5196 free, or the overridden first port plus the fixed fixture ports.

Playwright retains failure traces and screenshots and writes an HTML report.
Inspect these together with browser console and network errors before changing
the application. The fixtures are test infrastructure, not deployment servers.

## Capture v4 presentation evidence

The Home, Folder, service and layer detail, Styles, Layer Preview and scale
presentation fixtures capture 1440- and 390-pixel views in both light and dark
themes. These images are taken before keyboard or form interaction checks change
the rendered state; mobile scale captures show the open navigation drawer.
Existing 1280- and 390-pixel behavior checks remain in place.

CI uploads the images with the corresponding presentation artifacts. Files named
`<fixture>-fidelity-<width>-<theme>.png` record rendered output, theme activation
and absence of horizontal page overflow. They are review evidence, not approved
pixel baselines or proof of a live customer journey. Compare them with the
adopted design references before approving a visual baseline.

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

### Installed gate navigation smoke

The **Console E2E** workflow accepts a manual `stack_url` input (HTTP(S) stack
origin or `/console/` root). Manual dispatch runs only the installed navigation
smoke, at 1280x900 and 390x844. The ordinary push/PR jobs remain backend-free
regressions; their compact-navigation coverage was repaired in #814.

Configure `HONUA_GATE_OPERATOR_STATE_JSON` with Playwright storage state from an
existing authorized human session for that stack before dispatching. A missing
session fails the run. The workflow writes it to a private temporary file and
removes it on exit. It never uploads storage, traces, videos or screenshots.

For a verification lane holding its session locally:

```bash
cd e2e/playwright
HONUA_GATE_STACK_URL=https://example.test \
HONUA_WORKSPACE_E2E_OPERATOR_STATE=/private/operator-state.json \
npm run e2e:gate
```

This consumes the installed shipping bundle without local servers, mocked APIs,
admin keys or automated login. It checks responsive keyboard navigation and
route headings for Services and Health, plus page errors and horizontal overflow.
Record the stack's installed identities with the result. A navigation pass does
not establish Studio approval handoff, native AI completion or a release gate
pass; those journeys still require their own live evidence.
