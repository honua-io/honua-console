---
type: guide
title: "Run Console locally and in staging"
description: "Develop the static Console and verify a staged browser bundle against Honua Server."
tags: [deployment, local, staging]
---
# Local and Staging Startup

Console is a static Blazor WebAssembly SPA mounted at `/console/`. Honua Server
provides the API and authentication endpoints. Start with
[Static SPA deployment](STATIC_SPA.md) for the runtime configuration, security
headers and migration gates, and [Build artifact contract](BUILD_ARTIFACT.md)
for the release ZIP and receipt.

## Prerequisites

- .NET SDK 11.0.100, pinned by `src/Honua.Console.Web/global.json`.
- Node.js 20 or later for pinned browser assets and the smoke/e2e harnesses.
- Python 3 for static release packaging.
- A compatible Honua Server for live data and sign-in.

## Local development

From the repository root:

```bash
cp src/Honua.Console.Web/global.json global.json
dotnet restore Honua.Console.slnx --configfile NuGet.config --locked-mode
dotnet run --project src/Honua.Console.Web/Honua.Console.Web.csproj -- --urls http://127.0.0.1:5174 --pathbase=/console
```

Open `http://127.0.0.1:5174/console/`. Studio, Catalog, Operate and Share are
under `/console/studio`, `/console/catalog`, `/console/operate` and
`/console/share`. The development server serves browser files; it does not proxy
Honua API requests or read server-side Console environment variables.

Configure `src/Honua.Console.Web/wwwroot/config.json` using the public settings
in [Static SPA deployment](STATIC_SPA.md). The checked-in `apiBaseUrl: "/"`
expects API endpoints at the same origin as Console. For a local same-origin
setup, put a reverse proxy in front of the development server and Honua Server:
route `/console/` to Console and the API and authentication paths to Honua Server.
Use the proxy origin in the browser. Plain HTTP is accepted only on loopback;
the production CSP also requires plain-HTTP API traffic to share Console's origin.

Alternatively, set `apiBaseUrl` to the HTTPS Honua API origin and configure
Honua Server CORS for the exact Console origin. This uses an operator bearer in
browser memory. Same-origin sign-in uses the server cookie and CSRF protection.
OIDC requires the same-origin server broker; configure it as described in
[Static SPA deployment](STATIC_SPA.md#release-status).

Keep credentials out of `config.json` and the static bundle. `HONUA_SERVER_BASE_URL`
and `HONUA_ADMIN_API_KEY` belong to legacy test-host configurations; setting them
on a static host does not configure the browser app.

## Verifying the production artifact locally

Produce the ZIP and JSON receipt using [Build artifact contract](BUILD_ARTIFACT.md).
For a local asset and navigation check, serve the published directory with the
repository's static test server:

```bash
HONUA_CONSOLE_STATIC_ROOT=artifacts/static-publish/wwwroot/console \
HONUA_CONSOLE_STATIC_PORT=5184 \
node e2e/playwright/static-bundle-server.mjs
```

Open `http://127.0.0.1:5184/console/`. This helper supplies static files and SPA
navigation fallback only; it does not start or proxy a live Honua API. Use the
same-origin proxy or HTTPS API configuration above for server-backed checks.
The helper is a development fixture; production headers and caching must follow
[Static SPA deployment](STATIC_SPA.md).

Check that:

1. `/console/config.json` contains the intended public API/auth settings.
2. The browser loads framework, Studio, CSS and JavaScript files from `/console/`
   without missing assets or MIME-type failures.
3. A direct navigation and reload of `/console/operate` returns the app shell.
4. Missing asset requests return 404 instead of `index.html`.
5. The ZIP digest matches the companion receipt before deploying those bytes.

The static artifact contains no runnable Console application server. Release
identity comes from the ZIP receipt; `/version.json` is a legacy test-host endpoint.

## Staging verification

Deploy the release ZIP under `/console/`, set public runtime configuration and
apply the headers and cache rules in [Static SPA deployment](STATIC_SPA.md).
Record the Console ZIP receipt and Honua Server version with the evidence.

Verify direct navigation and reload on all four main surfaces, then sign in as
an operator and exercise a permitted read and an approval mutation. Confirm that
same-origin mutations send the CSRF header with the server cookie, or that
separate-origin requests carry the operator bearer and pass CORS. A missing or
expired session must produce an explicit sign-in or permission state.

For OIDC, verify that `/admin/auth/callback` reaches Honua Server, that the server
returns to an allowlisted `/console/` path and that the authenticated session
survives navigation. Check the served CSP with:

```bash
node scripts/console-csp.mjs --verify-origin https://your-console.example
```

A successful build or fixture run does not establish installed browser usability.
Record the live journey separately; the remaining qualification requirements are
listed in [Static SPA deployment](STATIC_SPA.md#release-status).

## Checks and legacy integration fixtures

Documentation gates:

```bash
python3 scripts/check-okf-bundle.py
python3 scripts/generate-summary.py --check
```

Use `./scripts/fast-local-check.sh` for the shared Shell tests and browser build,
`npm test` for Node checks and `npm run smoke:parity` for in-process contract shapes.
The static-bundle workflow publishes and packages the browser output; the exact
preparation and verification commands are in [Build artifact contract](BUILD_ARTIFACT.md).

The opt-in `OperateTransitionLiveServerTests`, `ConsoleEndToEndSmokeTests` and
other Testcontainers suites retain server-side test fixtures. Their flags and
commands remain in the README's [live-server test lanes](../../README.md#opt-in-live-server-test-lanes).
Their API keys, BFF cookies and `/version.json` checks describe those fixtures;
they do not qualify the shipped static browser session. For browser harnesses and
evidence requirements, see [Agent Browser Testing](../testing/AGENT_BROWSER_TESTING.md)
and [the e2e harness](../../e2e/README.md).
